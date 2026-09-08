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
    /// itself) without suppressing genuine differences found on the way down. A result that was
    /// itself short-circuited by a pair other than itself is not memoised, because it is only the
    /// answer for positions beneath that other pair; reached from anywhere else, the pair is
    /// walked again and reports everything it finds there. Memoised changes carry locations
    /// relative to the pair they were found in, so a type reached from several places reports the
    /// same changes once at each of those places, each with its own path.
    /// </summary>
    private sealed class Differ
    {
        private readonly bool _includeMetadata;
        private readonly Dictionary<SchemaPair, List<SchemaChange>> _memo = [];

        /// <summary>The pairs currently being computed, each mapped to its depth on the stack.</summary>
        private readonly Dictionary<SchemaPair, int> _inFlight = [];

        /// <summary>
        /// The shallowest in-flight pair the computation in progress short-circuited on, or
        /// <see cref="int.MaxValue"/> when it short-circuited on none. A result that stopped at a
        /// pair further up the stack is only complete beneath that pair, so it must not be
        /// memoised: reached from elsewhere, the same pair can have more to report.
        /// </summary>
        private int _truncatedAtDepth = int.MaxValue;

        public Differ(bool includeMetadata)
        {
            _includeMetadata = includeMetadata;
        }

        public List<SchemaChange> Calculate(Schema before, Schema after, string location) =>
            Rebase(CalculateRelative(before, after), location);

        /// <summary>
        /// Computes the changes between a pair with locations relative to that pair's own root,
        /// which is what makes the memoised result reusable from any position.
        /// </summary>
        private List<SchemaChange> CalculateRelative(Schema before, Schema after)
        {
            var structural = CalculateStructuralRelative(Unwrap(before), Unwrap(after));

            // Logical types are compared outside the memoised structural walk, which is keyed on
            // the unwrapped pair: the same base schemas can be reached carrying different (or no)
            // logical annotations, so a logical difference must never be baked into a cache entry
            // shared with a pair that doesn't have it.
            var logical = new List<SchemaChange>();
            CompareLogicalTypes(logical, before, after, RelativeRoot);

            return logical.Count == 0 ? structural : [.. structural, .. logical];
        }

        /// <summary>
        /// Compares the structure of an already-unwrapped pair, memoised by pair identity.
        /// </summary>
        private List<SchemaChange> CalculateStructuralRelative(Schema before, Schema after)
        {
            var pair = new SchemaPair(before, after);
            if (_memo.TryGetValue(pair, out var cached))
                return cached;

            if (_inFlight.TryGetValue(pair, out var inFlightDepth))
            {
                // Recursion in progress at this pair; the outer call owns the real result. Record
                // how far up the stack that call is, so nothing truncated by it is memoised.
                _truncatedAtDepth = Math.Min(_truncatedAtDepth, inFlightDepth);
                return [];
            }

            var depth = _inFlight.Count;
            _inFlight[pair] = depth;

            var callerTruncatedAtDepth = _truncatedAtDepth;
            _truncatedAtDepth = int.MaxValue;

            var sink = new List<SchemaChange>();
            Compute(sink, before, after, RelativeRoot);

            var truncatedAtDepth = _truncatedAtDepth;
            _inFlight.Remove(pair);

            // A short circuit back to this pair itself is resolved here: this call is the outer one
            // that owns the real result, and that result holds wherever the pair is reached.
            if (truncatedAtDepth >= depth)
            {
                _memo[pair] = sink;
                _truncatedAtDepth = callerTruncatedAtDepth;
            }
            else
            {
                _truncatedAtDepth = Math.Min(callerTruncatedAtDepth, truncatedAtDepth);
            }

            return sink;
        }

        private static List<SchemaChange> Rebase(List<SchemaChange> relative, string location)
        {
            var rebased = new List<SchemaChange>(relative.Count);
            foreach (var change in relative)
            {
                rebased.Add(new SchemaChange(
                    change.Kind,
                    Combine(location, change.Location),
                    change.Message,
                    change.OldValue,
                    change.NewValue,
                    change.IsValidPromotion));
            }

            return rebased;
        }

        private void Compute(List<SchemaChange> sink, Schema before, Schema after, string location)
        {
            var beforeType = ComparisonType(before);
            var afterType = ComparisonType(after);

            if (beforeType != afterType)
            {
                sink.Add(new SchemaChange(
                    ChangeKind.TypeKindChanged,
                    location,
                    $"type changed from {TypeName(before)} to {TypeName(after)}",
                    oldValue: TypeName(before),
                    newValue: TypeName(after),
                    isValidPromotion: IsPromotable(readerType: afterType, writerType: beforeType)));
                return;
            }

            switch (beforeType)
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
                    if (CheckNamedTypeIdentity(sink, (NamedSchema)before, (NamedSchema)after, location))
                    {
                        CompareFields(sink, (RecordSchema)before, (RecordSchema)after, location);
                        if (_includeMetadata)
                        {
                            CompareNamedTypeMetadata(sink, (NamedSchema)before, (NamedSchema)after, location);
                            CompareRecordDeclaration(sink, (RecordSchema)before, (RecordSchema)after, location);
                        }
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
        /// into a misleading field-by-field diff of two unrelated types. When the link was made
        /// through an alias the type carries on being compared, but the name it is known by has
        /// changed, which is reported as a <see cref="ChangeKind.TypeRenamed"/> in its own right:
        /// the full name is what the canonical form, the fingerprint and any generated code are
        /// built from.
        /// </summary>
        private static bool CheckNamedTypeIdentity(List<SchemaChange> sink, NamedSchema before, NamedSchema after, string location)
        {
            if (string.Equals(before.Fullname, after.Fullname, StringComparison.Ordinal))
                return true;

            if (LinkedByAlias(before, after))
            {
                sink.Add(new SchemaChange(
                    ChangeKind.TypeRenamed,
                    location,
                    $"type renamed from {before.Fullname} to {after.Fullname}",
                    oldValue: before.Fullname,
                    newValue: after.Fullname));
                return true;
            }

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

            var beforeAliases = NamedSchemaAliases(before).ToHashSet(StringComparer.Ordinal);
            var afterAliases = NamedSchemaAliases(after).ToHashSet(StringComparer.Ordinal);
            if (!beforeAliases.SetEquals(afterAliases))
            {
                sink.Add(new SchemaChange(
                    ChangeKind.MetadataChanged,
                    Append(location, "aliases"),
                    "aliases changed",
                    oldValue: string.Join(", ", beforeAliases),
                    newValue: string.Join(", ", afterAliases)));
            }
        }

        /// <summary>
        /// Reports a record that became a protocol error, or an error that became a plain record.
        /// Both hold the same fields and read each other's data, so this is not a change of shape,
        /// but the declaration decides how generated code models the type.
        /// </summary>
        private static void CompareRecordDeclaration(List<SchemaChange> sink, RecordSchema before, RecordSchema after, string location)
        {
            if (before.Tag == after.Tag)
                return;

            sink.Add(new SchemaChange(
                ChangeKind.MetadataChanged,
                Append(location, "type"),
                $"declaration changed from {DeclarationKeyword(before)} to {DeclarationKeyword(after)}",
                oldValue: DeclarationKeyword(before),
                newValue: DeclarationKeyword(after)));
        }

        private static string DeclarationKeyword(RecordSchema schema) =>
            schema.Tag == Schema.Type.Error ? "error" : "record";

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
            var beforeByKey = BranchesByKey(before);
            var afterByKey = BranchesByKey(after);
            var matches = MatchBranches(beforeByKey, afterByKey);
            var matchedBeforeKeys = matches.Values.ToHashSet(StringComparer.Ordinal);

            foreach (var (key, afterBranch) in afterByKey)
            {
                if (matches.TryGetValue(key, out var beforeKey))
                    sink.AddRange(Calculate(beforeByKey[beforeKey], afterBranch, Append(location, key)));
                else
                    sink.Add(new SchemaChange(ChangeKind.UnionBranchAdded, location, $"branch added: {BranchName(afterBranch)}"));
            }

            foreach (var (key, beforeBranch) in beforeByKey)
            {
                if (!matchedBeforeKeys.Contains(key))
                    sink.Add(new SchemaChange(ChangeKind.UnionBranchRemoved, location, $"branch removed: {BranchName(beforeBranch)}"));
            }

            CompareUnionBranchOrder(sink, beforeByKey, afterByKey, matches, location);
        }

        /// <summary>
        /// Pairs up the branches of the two unions, returning the "before" branch each "after"
        /// branch was paired with, keyed by the "after" branch's name. Branches sharing a name pair
        /// with each other; a named branch left unpaired then pairs with an unpaired named branch
        /// linked to it by an alias in either direction, so that a type renamed between the two
        /// versions is diffed as the one branch it is, exactly as it would be at a field position,
        /// rather than being reported as an unrelated branch removed and another added.
        /// </summary>
        private static Dictionary<string, string> MatchBranches(
            OrderedDictionary<string, Schema> beforeByKey,
            OrderedDictionary<string, Schema> afterByKey)
        {
            var matches = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in afterByKey.Keys)
            {
                if (beforeByKey.ContainsKey(key))
                    matches[key] = key;
            }

            var unpairedBefore = beforeByKey
                .Where(branch => !afterByKey.ContainsKey(branch.Key) && Unwrap(branch.Value) is NamedSchema)
                .Select(branch => (branch.Key, Named: (NamedSchema)Unwrap(branch.Value)))
                .ToList();

            foreach (var (afterKey, afterBranch) in afterByKey)
            {
                if (unpairedBefore.Count == 0)
                    break;
                if (matches.ContainsKey(afterKey) || Unwrap(afterBranch) is not NamedSchema afterNamed)
                    continue;

                var index = unpairedBefore.FindIndex(branch => LinkedByAlias(branch.Named, afterNamed));
                if (index < 0)
                    continue;

                matches[afterKey] = unpairedBefore[index].Key;
                unpairedBefore.RemoveAt(index);
            }

            return matches;
        }

        /// <summary>
        /// Reports a change to the order in which a union declares its branches. The order is part
        /// of the schema's meaning: it fixes the index each branch is written under in binary
        /// encoding, it is preserved in the parsing canonical form and therefore in the
        /// fingerprint, and it decides which branch a default value has to belong to. Only the
        /// branches paired across both sides are compared, so the shift that inevitably follows a
        /// branch being added or removed is left to the addition or removal to describe.
        /// </summary>
        private static void CompareUnionBranchOrder(
            List<SchemaChange> sink,
            OrderedDictionary<string, Schema> beforeByKey,
            OrderedDictionary<string, Schema> afterByKey,
            Dictionary<string, string> matches,
            string location)
        {
            // Both sequences are expressed in "before" keys, so that a branch paired through an
            // alias is compared by position rather than counting as a move.
            var matchedBeforeKeys = matches.Values.ToHashSet(StringComparer.Ordinal);
            var beforeCommon = beforeByKey.Keys.Where(matchedBeforeKeys.Contains);
            var afterCommon = afterByKey.Keys.Where(matches.ContainsKey).Select(key => matches[key]);

            if (beforeCommon.SequenceEqual(afterCommon, StringComparer.Ordinal))
                return;

            var oldOrder = string.Join(", ", beforeByKey.Values.Select(BranchName));
            var newOrder = string.Join(", ", afterByKey.Values.Select(BranchName));

            sink.Add(new SchemaChange(
                ChangeKind.UnionBranchesReordered,
                location,
                $"branch order changed from [{oldOrder}] to [{newOrder}]",
                oldValue: oldOrder,
                newValue: newOrder));
        }

        /// <summary>
        /// The branches of a union, keyed for matching and kept in the order they are declared. A
        /// key that appears more than once keeps its first branch, so that every key names exactly
        /// one branch on each side of the comparison.
        /// </summary>
        private static OrderedDictionary<string, Schema> BranchesByKey(UnionSchema union)
        {
            var branches = new OrderedDictionary<string, Schema>(union.Count, StringComparer.Ordinal);
            foreach (var branch in union.Schemas)
                branches.TryAdd(BranchName(branch), branch);

            return branches;
        }

        /// <summary>
        /// Names a union branch: its full name when it is a named type, otherwise the Avro name of
        /// its type. A branch is matched, located and described by this name. Naming goes by the
        /// underlying representation, so a branch that gains or loses a logical type is still
        /// recognised as the same branch and diffed as a change to it.
        /// </summary>
        private static string BranchName(Schema schema) =>
            Unwrap(schema) is NamedSchema named ? named.Fullname : TypeName(Unwrap(schema));

        /// <summary>
        /// Reports a <c>logicalType</c> that was added, removed or replaced, and — when both sides
        /// carry the same logical type — any change to the attributes qualifying it.
        /// </summary>
        private static void CompareLogicalTypes(List<SchemaChange> sink, Schema before, Schema after, string location)
        {
            var beforeName = (before as LogicalSchema)?.LogicalTypeName;
            var afterName = (after as LogicalSchema)?.LogicalTypeName;

            if (!string.Equals(beforeName, afterName, StringComparison.Ordinal))
            {
                var message = (beforeName, afterName) switch
                {
                    (null, not null) => $"logical type '{afterName}' added",
                    (not null, null) => $"logical type '{beforeName}' removed",
                    _ => $"logical type changed from '{beforeName}' to '{afterName}'",
                };

                sink.Add(new SchemaChange(
                    ChangeKind.LogicalTypeChanged,
                    Append(location, "logicalType"),
                    message,
                    oldValue: beforeName,
                    newValue: afterName));
                return;
            }

            if (beforeName == DecimalLogicalTypeName)
                CompareDecimalAttributes(sink, (LogicalSchema)before, (LogicalSchema)after, location);
        }

        private static void CompareDecimalAttributes(List<SchemaChange> sink, LogicalSchema before, LogicalSchema after, string location)
        {
            CompareDecimalAttribute(sink, "precision", before.GetProperty("precision"), after.GetProperty("precision"), location);

            // Avro makes scale optional and defaults it to zero, so an omitted scale and an
            // explicit zero describe the same type and must not read as a change.
            CompareDecimalAttribute(
                sink,
                "scale",
                before.GetProperty("scale") ?? DefaultDecimalScale,
                after.GetProperty("scale") ?? DefaultDecimalScale,
                location);
        }

        private static void CompareDecimalAttribute(List<SchemaChange> sink, string name, string? before, string? after, string location)
        {
            if (string.Equals(before, after, StringComparison.Ordinal))
                return;

            sink.Add(new SchemaChange(
                ChangeKind.LogicalTypeAttributeChanged,
                Append(location, name),
                $"decimal {name} changed from {before ?? "unset"} to {after ?? "unset"}",
                oldValue: before,
                newValue: after));
        }

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
                var typeChanges = CalculateRelative(matchedBefore.Schema, matchedAfter.Schema);

                // A tag mismatch (or unrelated named-type swap) found directly at the field's own
                // type is the common, important case the spec calls out as "field type changed";
                // reuse the same detection emitted by Calculate for nested structures (array items,
                // union branches, etc.), but relabel it when it's this field's own immediate type
                // rather than something changed deeper inside it. Such a change short-circuits the
                // structural walk, so it is always the first entry and sits at the pair's own root;
                // anything after it is a logical type change on the same pair, reported as it is.
                var rebasedTypeChanges = Rebase(typeChanges, typeLocation);
                if (typeChanges.Count > 0 && typeChanges[0].Kind == ChangeKind.TypeKindChanged && typeChanges[0].Location.Length == 0)
                {
                    var change = typeChanges[0];
                    sink.Add(new SchemaChange(
                        ChangeKind.FieldTypeChanged,
                        typeLocation,
                        change.Message,
                        change.OldValue,
                        change.NewValue,
                        change.IsValidPromotion));
                    sink.AddRange(rebasedTypeChanges.Skip(1));
                }
                else
                {
                    sink.AddRange(rebasedTypeChanges);
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

    /// <summary>The type promotions permitted by the Avro specification, keyed by reader type.</summary>
    private static readonly Dictionary<Schema.Type, HashSet<Schema.Type>> PromotableWriterTypes = new()
    {
        [Schema.Type.Long] = [Schema.Type.Int],
        [Schema.Type.Float] = [Schema.Type.Int, Schema.Type.Long],
        [Schema.Type.Double] = [Schema.Type.Int, Schema.Type.Long, Schema.Type.Float],
        [Schema.Type.Bytes] = [Schema.Type.String],
        [Schema.Type.String] = [Schema.Type.Bytes],
    };

    /// <summary>
    /// Whether a reader using <paramref name="readerType"/> can read data written with
    /// <paramref name="writerType"/> under Avro's type promotion rules. Promotion is
    /// directional: <c>int</c> data reads back as <c>long</c>, but not the other way round.
    /// </summary>
    private static bool IsPromotable(Schema.Type readerType, Schema.Type writerType) =>
        PromotableWriterTypes.TryGetValue(readerType, out var writers) && writers.Contains(writerType);

    private static Schema Unwrap(Schema schema) =>
        schema is LogicalSchema logical ? logical.BaseSchema : schema;

    /// <summary>
    /// The name the Avro specification gives a schema's type (<c>int</c>, <c>enum</c>, …), which is
    /// what a schema document is written in and therefore what a reader of a report expects to see.
    /// </summary>
    private static string TypeName(Schema schema) => Schema.GetTypeString(schema.Tag);

    /// <summary>
    /// Whether two named schemas are the same type under different names, i.e. one of them lists
    /// the other's full name among its aliases.
    /// </summary>
    private static bool LinkedByAlias(NamedSchema before, NamedSchema after) =>
        NamedSchemaAliases(before).Contains(after.Fullname, StringComparer.Ordinal) ||
        NamedSchemaAliases(after).Contains(before.Fullname, StringComparer.Ordinal);

    /// <summary>
    /// The type a schema is compared as. A protocol error is a record that carries an error flag,
    /// so both are compared as <see cref="Schema.Type.Record"/> and a type that changed between the
    /// two is diffed field by field instead of being reported as a wholesale replacement.
    /// </summary>
    private static Schema.Type ComparisonType(Schema schema) =>
        schema.Tag == Schema.Type.Error ? Schema.Type.Record : schema.Tag;

    private const string DecimalLogicalTypeName = "decimal";

    /// <summary>The scale Avro assumes for a decimal that doesn't declare one.</summary>
    private const string DefaultDecimalScale = "0";

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

    /// <summary>The location of the pair currently being computed, which every change hangs off.</summary>
    private const string RelativeRoot = "";

    /// <summary>Joins a location prefix to a suffix, either of which may be the empty relative root.</summary>
    private static string Combine(string prefix, string suffix)
    {
        if (suffix.Length == 0)
            return prefix;
        if (prefix.Length == 0)
            return suffix;

        return prefix.EndsWith('/') ? prefix + suffix : prefix + "/" + suffix;
    }

    private static string Append(string location, string segment) => Combine(location, segment);

    private static string Append(string location, string first, string second) =>
        Append(Append(location, first), second);
}
