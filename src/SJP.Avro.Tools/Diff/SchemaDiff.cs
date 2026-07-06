using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avro;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SJP.Avro.Tools.Diff;

/// <summary>
/// Computes a semantic, field-level diff between two versions of an Avro schema. Where
/// <see cref="Compatibility.SchemaCompatibility"/> answers whether a change is safe, this answers
/// what changed.
/// </summary>
public static class SchemaDiff
{
    /// <summary>
    /// Compares the <paramref name="before"/> schema against the <paramref name="after"/> schema.
    /// </summary>
    /// <param name="before">The earlier version of the schema.</param>
    /// <param name="after">The later version of the schema.</param>
    /// <param name="includeMetadata">
    /// When <c>true</c>, also reports documentation, alias and enum-default changes that don't
    /// affect the schema's shape.
    /// </param>
    /// <returns>A result listing every change detected, in traversal order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="before"/> or <paramref name="after"/> is <c>null</c>.</exception>
    public static SchemaDiffResult Compare(Schema before, Schema after, bool includeMetadata = false)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var differ = new Differ(includeMetadata);
        var changes = differ.Calculate(before, after, "/");
        return new SchemaDiffResult(changes);
    }

    /// <summary>
    /// The recursion state for a single top-level comparison. Unlike compatibility checking, a
    /// pair of schemas still being computed cannot be assumed "unchanged" as a permanent answer,
    /// so an in-flight pair only ever short-circuits the single reentrant call that found it still
    /// in progress: the outer call keeps walking and its real (possibly non-empty) result is what
    /// gets memoised. This terminates recursive schemas (e.g. a record containing an array of
    /// itself) without suppressing genuine differences found on the way down.
    /// </summary>
    private sealed class Differ
    {
        private readonly bool _includeMetadata;
        private readonly Dictionary<SchemaPair, List<SchemaChange>> _memo = [];
        private readonly HashSet<SchemaPair> _inFlight = [];

        public Differ(bool includeMetadata)
        {
            _includeMetadata = includeMetadata;
        }

        public List<SchemaChange> Calculate(Schema before, Schema after, string location)
        {
            before = Unwrap(before);
            after = Unwrap(after);

            var pair = new SchemaPair(before, after);
            if (_memo.TryGetValue(pair, out var cached))
                return cached;

            if (!_inFlight.Add(pair))
                return []; // recursion in progress at this pair; the outer call owns the real result

            var sink = new List<SchemaChange>();
            Compute(sink, before, after, location);

            _inFlight.Remove(pair);
            _memo[pair] = sink;
            return sink;
        }

        private void Compute(List<SchemaChange> sink, Schema before, Schema after, string location)
        {
            if (before.Tag != after.Tag)
            {
                sink.Add(new SchemaChange(
                    ChangeKind.TypeKindChanged,
                    location,
                    $"type changed from {before.Tag.ToString().ToUpperInvariant()} to {after.Tag.ToString().ToUpperInvariant()}",
                    oldValue: before.Tag.ToString(),
                    newValue: after.Tag.ToString(),
                    isValidPromotion: IsPromotable(before.Tag, after.Tag) || IsPromotable(after.Tag, before.Tag)));
                return;
            }

            switch (before.Tag)
            {
                case Schema.Type.Null:
                case Schema.Type.Boolean:
                case Schema.Type.Int:
                case Schema.Type.Long:
                case Schema.Type.Float:
                case Schema.Type.Double:
                case Schema.Type.Bytes:
                case Schema.Type.String:
                    break; // identical primitive types: no change

                case Schema.Type.Array:
                    sink.AddRange(Calculate(((ArraySchema)before).ItemSchema, ((ArraySchema)after).ItemSchema, Append(location, "items")));
                    break;

                case Schema.Type.Map:
                    sink.AddRange(Calculate(((MapSchema)before).ValueSchema, ((MapSchema)after).ValueSchema, Append(location, "values")));
                    break;

                case Schema.Type.Fixed:
                    if (CheckNamedTypeIdentity(sink, (NamedSchema)before, (NamedSchema)after, location))
                    {
                        CompareFixed(sink, (FixedSchema)before, (FixedSchema)after, location);
                        if (_includeMetadata)
                            CompareNamedTypeMetadata(sink, (NamedSchema)before, (NamedSchema)after, location);
                    }

                    break;

                case Schema.Type.Enumeration:
                    if (CheckNamedTypeIdentity(sink, (NamedSchema)before, (NamedSchema)after, location))
                    {
                        CompareEnum(sink, (EnumSchema)before, (EnumSchema)after, location);
                        if (_includeMetadata)
                            CompareNamedTypeMetadata(sink, (NamedSchema)before, (NamedSchema)after, location);
                    }

                    break;

                case Schema.Type.Record:
                case Schema.Type.Error:
                    if (CheckNamedTypeIdentity(sink, (NamedSchema)before, (NamedSchema)after, location))
                    {
                        CompareFields(sink, (RecordSchema)before, (RecordSchema)after, location);
                        if (_includeMetadata)
                            CompareNamedTypeMetadata(sink, (NamedSchema)before, (NamedSchema)after, location);
                    }

                    break;

                case Schema.Type.Union:
                    CompareUnion(sink, (UnionSchema)before, (UnionSchema)after, location);
                    break;

                default:
                    break;
            }
        }

        /// <summary>
        /// Checks whether two named schemas reached at the same structural position represent
        /// "the same" type across versions (identical name, or linked by an alias on either side).
        /// If not, reports a single <see cref="ChangeKind.TypeKindChanged"/> rather than cascading
        /// into a misleading field-by-field diff of two unrelated types.
        /// </summary>
        private static bool CheckNamedTypeIdentity(List<SchemaChange> sink, NamedSchema before, NamedSchema after, string location)
        {
            if (string.Equals(before.Fullname, after.Fullname, StringComparison.Ordinal))
                return true;

            if (NamedSchemaAliases(before).Contains(after.Fullname, StringComparer.Ordinal) ||
                NamedSchemaAliases(after).Contains(before.Fullname, StringComparer.Ordinal))
                return true;

            sink.Add(new SchemaChange(
                ChangeKind.TypeKindChanged,
                location,
                $"type changed from {before.Fullname} to {after.Fullname}",
                oldValue: before.Fullname,
                newValue: after.Fullname));
            return false;
        }

        private static void CompareNamedTypeMetadata(List<SchemaChange> sink, NamedSchema before, NamedSchema after, string location)
        {
            if (!string.Equals(before.Documentation, after.Documentation, StringComparison.Ordinal))
            {
                sink.Add(new SchemaChange(
                    ChangeKind.MetadataChanged,
                    Append(location, "doc"),
                    "documentation changed",
                    oldValue: before.Documentation,
                    newValue: after.Documentation));
            }
        }

        private static void CompareFixed(List<SchemaChange> sink, FixedSchema before, FixedSchema after, string location)
        {
            if (before.Size != after.Size)
            {
                sink.Add(new SchemaChange(
                    ChangeKind.FixedSizeChanged,
                    Append(location, "size"),
                    $"size changed from {before.Size} to {after.Size}",
                    oldValue: before.Size.ToString(CultureInfo.InvariantCulture),
                    newValue: after.Size.ToString(CultureInfo.InvariantCulture)));
            }
        }

        private void CompareEnum(List<SchemaChange> sink, EnumSchema before, EnumSchema after, string location)
        {
            var beforeSymbols = before.Symbols;
            var afterSymbols = after.Symbols;

            var added = afterSymbols.Except(beforeSymbols).ToList();
            var removed = beforeSymbols.Except(afterSymbols).ToList();

            if (added.Count > 0)
            {
                sink.Add(new SchemaChange(
                    ChangeKind.EnumSymbolAdded,
                    Append(location, "symbols"),
                    "[" + string.Join(", ", added) + "]"));
            }

            if (removed.Count > 0)
            {
                sink.Add(new SchemaChange(
                    ChangeKind.EnumSymbolRemoved,
                    Append(location, "symbols"),
                    "[" + string.Join(", ", removed) + "]"));
            }

            if (added.Count == 0 && removed.Count == 0 && !beforeSymbols.SequenceEqual(afterSymbols))
            {
                sink.Add(new SchemaChange(
                    ChangeKind.EnumSymbolsReordered,
                    Append(location, "symbols"),
                    $"order changed from [{string.Join(", ", beforeSymbols)}] to [{string.Join(", ", afterSymbols)}]",
                    oldValue: string.Join(", ", beforeSymbols),
                    newValue: string.Join(", ", afterSymbols)));
            }

            if (_includeMetadata && !string.Equals(before.Default, after.Default, StringComparison.Ordinal))
            {
                sink.Add(new SchemaChange(
                    ChangeKind.MetadataChanged,
                    Append(location, "default"),
                    $"default symbol changed from '{before.Default}' to '{after.Default}'",
                    oldValue: before.Default,
                    newValue: after.Default));
            }
        }

        private void CompareUnion(List<SchemaChange> sink, UnionSchema before, UnionSchema after, string location)
        {
            var beforeByKey = new Dictionary<string, Schema>();
            foreach (var branch in before.Schemas)
                beforeByKey.TryAdd(BranchKey(branch), branch);

            var afterByKey = new Dictionary<string, Schema>();
            foreach (var branch in after.Schemas)
                afterByKey.TryAdd(BranchKey(branch), branch);

            foreach (var entry in afterByKey)
            {
                if (beforeByKey.TryGetValue(entry.Key, out var beforeBranch))
                    sink.AddRange(Calculate(beforeBranch, entry.Value, Append(location, entry.Key)));
                else
                    sink.Add(new SchemaChange(ChangeKind.UnionBranchAdded, location, $"branch added: {DescribeBranch(entry.Value)}"));
            }

            foreach (var entry in beforeByKey)
            {
                if (!afterByKey.ContainsKey(entry.Key))
                    sink.Add(new SchemaChange(ChangeKind.UnionBranchRemoved, location, $"branch removed: {DescribeBranch(entry.Value)}"));
            }
        }

        private static string BranchKey(Schema schema) =>
            schema is NamedSchema named ? named.Fullname : schema.Tag.ToString();

        private static string DescribeBranch(Schema schema) =>
            schema is NamedSchema named ? named.Fullname : schema.Tag.ToString().ToUpperInvariant();

        /// <summary>
        /// Matches record fields across versions by name, then by alias (in either direction), and
        /// reports adds, removes, renames, type changes and default-value changes for the result.
        /// Renames are only ever detected via an explicit <c>aliases</c> link — two same-shaped but
        /// differently-named fields are never inferred to be "the same field, renamed": an unlinked
        /// rename is reported as a remove of the old field plus an add of the new one.
        /// </summary>
        private void CompareFields(List<SchemaChange> sink, RecordSchema before, RecordSchema after, string location)
        {
            var beforeByName = before.Fields.ToDictionary(f => f.Name);
            var afterByName = after.Fields.ToDictionary(f => f.Name);

            var consumedBeforeNames = new HashSet<string>();
            var consumedAfterNames = new HashSet<string>();
            var matches = new List<(Field Before, Field After, bool Renamed)>();

            foreach (var afterField in after.Fields)
            {
                if (beforeByName.TryGetValue(afterField.Name, out var sameNameBefore))
                {
                    consumedBeforeNames.Add(afterField.Name);
                    consumedAfterNames.Add(afterField.Name);
                    matches.Add((sameNameBefore, afterField, false));
                    continue;
                }

                var renameAlias = (afterField.Aliases ?? [])
                    .FirstOrDefault(alias => beforeByName.ContainsKey(alias) && !consumedBeforeNames.Contains(alias));
                if (renameAlias != null)
                {
                    consumedBeforeNames.Add(renameAlias);
                    consumedAfterNames.Add(afterField.Name);
                    matches.Add((beforeByName[renameAlias], afterField, true));
                }
            }

            foreach (var beforeField in before.Fields)
            {
                if (consumedBeforeNames.Contains(beforeField.Name))
                    continue;

                var renameAlias = (beforeField.Aliases ?? [])
                    .FirstOrDefault(alias => afterByName.ContainsKey(alias) && !consumedAfterNames.Contains(alias));
                if (renameAlias != null)
                {
                    consumedBeforeNames.Add(beforeField.Name);
                    consumedAfterNames.Add(renameAlias);
                    matches.Add((beforeField, afterByName[renameAlias], true));
                }
            }

            foreach (var afterField in after.Fields)
            {
                if (consumedAfterNames.Contains(afterField.Name))
                    continue;

                var hasDefault = afterField.DefaultValue != null;
                sink.Add(new SchemaChange(
                    ChangeKind.FieldAdded,
                    Append(location, "fields", afterField.Name),
                    hasDefault ? "field added with a default value" : "field added without a default value"));
            }

            foreach (var beforeField in before.Fields)
            {
                if (consumedBeforeNames.Contains(beforeField.Name))
                    continue;

                var hasDefault = beforeField.DefaultValue != null;
                sink.Add(new SchemaChange(
                    ChangeKind.FieldRemoved,
                    Append(location, "fields", beforeField.Name),
                    hasDefault ? "field removed (had a default value)" : "field removed (had no default value)"));
            }

            foreach (var (matchedBefore, matchedAfter, renamed) in matches)
            {
                var fieldLocation = Append(location, "fields", matchedAfter.Name);

                if (renamed)
                {
                    sink.Add(new SchemaChange(
                        ChangeKind.FieldRenamed,
                        fieldLocation,
                        $"field renamed from '{matchedBefore.Name}' to '{matchedAfter.Name}'",
                        oldValue: matchedBefore.Name,
                        newValue: matchedAfter.Name));
                }

                var typeLocation = Append(fieldLocation, "type");
                var typeChanges = Calculate(matchedBefore.Schema, matchedAfter.Schema, typeLocation);

                // A tag mismatch (or unrelated named-type swap) found directly at the field's own
                // type is the common, important case the spec calls out as "field type changed";
                // reuse the same detection emitted by Calculate for nested structures (array items,
                // union branches, etc.), but relabel it when it's this field's own immediate type
                // rather than something changed deeper inside it.
                if (typeChanges.Count == 1 && typeChanges[0].Kind == ChangeKind.TypeKindChanged && typeChanges[0].Location == typeLocation)
                {
                    var change = typeChanges[0];
                    sink.Add(new SchemaChange(
                        ChangeKind.FieldTypeChanged,
                        change.Location,
                        change.Message,
                        change.OldValue,
                        change.NewValue,
                        change.IsValidPromotion));
                }
                else
                {
                    sink.AddRange(typeChanges);
                }

                CompareFieldDefault(sink, matchedBefore, matchedAfter, fieldLocation);

                if (_includeMetadata)
                    CompareFieldMetadata(sink, matchedBefore, matchedAfter, fieldLocation);
            }
        }

        private static void CompareFieldDefault(List<SchemaChange> sink, Field before, Field after, string location)
        {
            var beforeDefault = before.DefaultValue;
            var afterDefault = after.DefaultValue;

            if (beforeDefault == null && afterDefault == null)
                return;

            if (beforeDefault == null)
            {
                sink.Add(new SchemaChange(
                    ChangeKind.FieldDefaultAdded,
                    Append(location, "default"),
                    $"default value added: {afterDefault!.ToString(Formatting.None)}",
                    newValue: afterDefault.ToString(Formatting.None)));
                return;
            }

            if (afterDefault == null)
            {
                sink.Add(new SchemaChange(
                    ChangeKind.FieldDefaultRemoved,
                    Append(location, "default"),
                    $"default value removed: {beforeDefault.ToString(Formatting.None)}",
                    oldValue: beforeDefault.ToString(Formatting.None)));
                return;
            }

            if (!JToken.DeepEquals(beforeDefault, afterDefault))
            {
                sink.Add(new SchemaChange(
                    ChangeKind.FieldDefaultChanged,
                    Append(location, "default"),
                    $"default value changed from {beforeDefault.ToString(Formatting.None)} to {afterDefault.ToString(Formatting.None)}",
                    oldValue: beforeDefault.ToString(Formatting.None),
                    newValue: afterDefault.ToString(Formatting.None)));
            }
        }

        private static void CompareFieldMetadata(List<SchemaChange> sink, Field before, Field after, string location)
        {
            if (!string.Equals(before.Documentation, after.Documentation, StringComparison.Ordinal))
            {
                sink.Add(new SchemaChange(
                    ChangeKind.MetadataChanged,
                    Append(location, "doc"),
                    "field documentation changed",
                    oldValue: before.Documentation,
                    newValue: after.Documentation));
            }

            var beforeAliases = (before.Aliases ?? []).ToHashSet(StringComparer.Ordinal);
            var afterAliases = (after.Aliases ?? []).ToHashSet(StringComparer.Ordinal);
            if (!beforeAliases.SetEquals(afterAliases))
            {
                sink.Add(new SchemaChange(
                    ChangeKind.MetadataChanged,
                    Append(location, "aliases"),
                    "field aliases changed",
                    oldValue: string.Join(", ", beforeAliases),
                    newValue: string.Join(", ", afterAliases)));
            }
        }

        /// <summary>An identity-based before/after pair, used to memoise results and guard recursion.</summary>
        private readonly struct SchemaPair : IEquatable<SchemaPair>
        {
            private readonly Schema _before;
            private readonly Schema _after;

            public SchemaPair(Schema before, Schema after)
            {
                _before = before;
                _after = after;
            }

            public bool Equals(SchemaPair other) =>
                ReferenceEquals(_before, other._before) && ReferenceEquals(_after, other._after);

            public override bool Equals(object? obj) => obj is SchemaPair other && Equals(other);

            public override int GetHashCode() =>
                HashCode.Combine(RuntimeHelpers.GetHashCode(_before), RuntimeHelpers.GetHashCode(_after));
        }
    }

    /// <summary>The type promotions permitted by the Avro specification, keyed by the "smaller" type.</summary>
    private static readonly Dictionary<Schema.Type, HashSet<Schema.Type>> PromotableTypes = new()
    {
        [Schema.Type.Long] = [Schema.Type.Int],
        [Schema.Type.Float] = [Schema.Type.Int, Schema.Type.Long],
        [Schema.Type.Double] = [Schema.Type.Int, Schema.Type.Long, Schema.Type.Float],
        [Schema.Type.Bytes] = [Schema.Type.String],
        [Schema.Type.String] = [Schema.Type.Bytes],
    };

    private static bool IsPromotable(Schema.Type readerType, Schema.Type writerType) =>
        PromotableTypes.TryGetValue(readerType, out var writers) && writers.Contains(writerType);

    private static Schema Unwrap(Schema schema) =>
        schema is LogicalSchema logical ? logical.BaseSchema : schema;

    // Named-type aliases are not surfaced publicly by Apache.Avro, so read the private backing
    // field. Cached, and defensively falls back to no aliases if the field ever moves.
    private static readonly FieldInfo? AliasesField =
        typeof(NamedSchema).GetField("aliases", BindingFlags.NonPublic | BindingFlags.Instance);

    private static IEnumerable<string> NamedSchemaAliases(NamedSchema schema)
    {
        if (AliasesField?.GetValue(schema) is not IEnumerable<SchemaName> aliases)
            return [];

        return aliases.Select(a => a.Fullname);
    }

    private static string Append(string location, string segment) =>
        location.EndsWith('/') ? location + segment : location + "/" + segment;

    private static string Append(string location, string first, string second) =>
        Append(Append(location, first), second);
}
