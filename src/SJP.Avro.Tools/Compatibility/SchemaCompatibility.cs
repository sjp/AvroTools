using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avro;

namespace SJP.Avro.Tools.Compatibility;

/// <summary>
/// Checks whether data written with one Avro schema can be read with another under the
/// specification's schema-resolution rules. The comparison is introspective: it reports
/// each incompatibility (kind and location) rather than relying on resolver exceptions,
/// mirroring the Java <c>SchemaCompatibility</c> API.
/// </summary>
public static class SchemaCompatibility
{
    /// <summary>
    /// Checks whether the <paramref name="reader"/> schema can read data written with the
    /// <paramref name="writer"/> schema.
    /// </summary>
    /// <param name="reader">The schema used by the consumer.</param>
    /// <param name="writer">The schema the data was written with.</param>
    /// <returns>A result listing any detected incompatibilities.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> or <paramref name="writer"/> is <c>null</c>.</exception>
    public static SchemaCompatibilityResult CheckReaderWriterCompatibility(Schema reader, Schema writer)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(writer);

        var checker = new Checker();
        var incompatibilities = checker.Calculate(reader, writer, "/");
        return new SchemaCompatibilityResult(incompatibilities);
    }

    /// <summary>
    /// The recursion state for a single top-level compatibility check. Compatibility of a
    /// reader/writer pair is independent of where it appears, so results are memoised per pair.
    /// A pair still being computed is treated as compatible, which terminates recursive schemas
    /// (a recursive type is compatible with itself), as in the Avro specification's resolution.
    /// </summary>
    private sealed class Checker
    {
        // null value => the pair is currently being computed (recursion guard).
        private readonly Dictionary<SchemaPair, List<Incompatibility>?> _memo = [];

        private static readonly List<Incompatibility> None = [];

        public List<Incompatibility> Calculate(Schema reader, Schema writer, string location)
        {
            // Logical types resolve on their underlying representation, so compare the base schemas.
            reader = Unwrap(reader);
            writer = Unwrap(writer);

            var pair = new SchemaPair(reader, writer);
            if (_memo.TryGetValue(pair, out var cached))
                return cached ?? None; // null => recursion in progress => assume compatible

            _memo[pair] = null;

            var sink = new List<Incompatibility>();
            Compute(sink, reader, writer, location);

            _memo[pair] = sink;
            return sink;
        }

        private void Compute(List<Incompatibility> sink, Schema reader, Schema writer, string location)
        {
            if (reader.Tag == writer.Tag)
            {
                ComputeSameType(sink, reader, writer, location);
                return;
            }

            // A writer union is readable when every branch it can emit is readable by the reader.
            if (writer.Tag == Schema.Type.Union)
            {
                foreach (var writerBranch in ((UnionSchema)writer).Schemas)
                    sink.AddRange(Calculate(reader, writerBranch, location));
                return;
            }

            // A reader union reads a non-union writer if any branch can read it.
            if (reader.Tag == Schema.Type.Union)
            {
                CheckReaderUnion(sink, (UnionSchema)reader, writer, location);
                return;
            }

            if (!IsPromotable(reader.Tag, writer.Tag))
                AddTypeMismatch(sink, reader, writer, location);
        }

        private void ComputeSameType(List<Incompatibility> sink, Schema reader, Schema writer, string location)
        {
            switch (reader.Tag)
            {
                case Schema.Type.Null:
                case Schema.Type.Boolean:
                case Schema.Type.Int:
                case Schema.Type.Long:
                case Schema.Type.Float:
                case Schema.Type.Double:
                case Schema.Type.Bytes:
                case Schema.Type.String:
                    break; // identical primitive types are always compatible

                case Schema.Type.Array:
                    sink.AddRange(Calculate(((ArraySchema)reader).ItemSchema, ((ArraySchema)writer).ItemSchema, Append(location, "items")));
                    break;

                case Schema.Type.Map:
                    sink.AddRange(Calculate(((MapSchema)reader).ValueSchema, ((MapSchema)writer).ValueSchema, Append(location, "values")));
                    break;

                case Schema.Type.Fixed:
                    CheckName(sink, (NamedSchema)reader, (NamedSchema)writer, location);
                    CheckFixedSize(sink, (FixedSchema)reader, (FixedSchema)writer, location);
                    break;

                case Schema.Type.Enumeration:
                    CheckName(sink, (NamedSchema)reader, (NamedSchema)writer, location);
                    CheckEnumSymbols(sink, (EnumSchema)reader, (EnumSchema)writer, location);
                    break;

                case Schema.Type.Record:
                case Schema.Type.Error:
                    CheckName(sink, (NamedSchema)reader, (NamedSchema)writer, location);
                    CheckFields(sink, (RecordSchema)reader, (RecordSchema)writer, location);
                    break;

                case Schema.Type.Union:
                    // Both are unions: every branch the writer can emit must be readable by the reader union.
                    foreach (var writerBranch in ((UnionSchema)writer).Schemas)
                        sink.AddRange(Calculate(reader, writerBranch, location));
                    break;

                default:
                    AddTypeMismatch(sink, reader, writer, location);
                    break;
            }
        }

        private void CheckReaderUnion(List<Incompatibility> sink, UnionSchema reader, Schema writer, string location)
        {
            var readable = reader.Schemas.Any(branch => Calculate(branch, writer, location).Count == 0);
            if (!readable)
            {
                sink.Add(new Incompatibility(
                    SchemaIncompatibilityType.MissingUnionBranch,
                    $"reader union lacking writer type: {writer.Tag.ToString().ToUpperInvariant()}",
                    location));
            }
        }

        private static void CheckName(List<Incompatibility> sink, NamedSchema reader, NamedSchema writer, string location)
        {
            if (!SchemaNameEquals(reader, writer))
            {
                sink.Add(new Incompatibility(
                    SchemaIncompatibilityType.NameMismatch,
                    $"expected: {writer.Fullname}",
                    Append(location, "name")));
            }
        }

        private static void CheckFixedSize(List<Incompatibility> sink, FixedSchema reader, FixedSchema writer, string location)
        {
            if (reader.Size != writer.Size)
            {
                sink.Add(new Incompatibility(
                    SchemaIncompatibilityType.FixedSizeMismatch,
                    $"expected: {writer.Size}, found: {reader.Size}",
                    Append(location, "size")));
            }
        }

        private static void CheckEnumSymbols(List<Incompatibility> sink, EnumSchema reader, EnumSchema writer, string location)
        {
            // A writer symbol the reader does not declare is resolvable only if the reader has an enum default.
            if (reader.Default != null)
                return;

            var missing = writer.Symbols.Where(symbol => !reader.Contains(symbol)).ToList();
            if (missing.Count > 0)
            {
                sink.Add(new Incompatibility(
                    SchemaIncompatibilityType.MissingEnumSymbols,
                    "[" + string.Join(", ", missing) + "]",
                    Append(location, "symbols")));
            }
        }

        private void CheckFields(List<Incompatibility> sink, RecordSchema reader, RecordSchema writer, string location)
        {
            foreach (var readerField in reader.Fields)
            {
                if (TryLookupWriterField(writer, readerField, out var writerField))
                {
                    sink.AddRange(Calculate(readerField.Schema, writerField.Schema, Append(location, "fields", readerField.Name)));
                }
                else if (readerField.DefaultValue == null)
                {
                    // The reader adds a field the writer never wrote, and offers no default to populate it.
                    sink.Add(new Incompatibility(
                        SchemaIncompatibilityType.ReaderFieldMissingDefaultValue,
                        readerField.Name,
                        Append(location, "fields", readerField.Name)));
                }
            }
        }

        private static void AddTypeMismatch(List<Incompatibility> sink, Schema reader, Schema writer, string location)
        {
            sink.Add(new Incompatibility(
                SchemaIncompatibilityType.TypeMismatch,
                $"reader type: {reader.Tag.ToString().ToUpperInvariant()} not compatible with writer type: {writer.Tag.ToString().ToUpperInvariant()}",
                location));
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

    private static bool IsPromotable(Schema.Type readerType, Schema.Type writerType) =>
        PromotableWriterTypes.TryGetValue(readerType, out var writers) && writers.Contains(writerType);

    private static Schema Unwrap(Schema schema) =>
        schema is LogicalSchema logical ? logical.BaseSchema : schema;

    /// <summary>
    /// Matches a reader field to a writer field by the reader field's name, then by each of the
    /// reader field's aliases, following the Avro specification's field-resolution rules.
    /// </summary>
    private static bool TryLookupWriterField(RecordSchema writer, Field readerField, out Field writerField)
    {
        if (writer.TryGetField(readerField.Name, out writerField))
            return true;

        if (readerField.Aliases != null)
        {
            foreach (var alias in readerField.Aliases)
            {
                if (writer.TryGetField(alias, out writerField))
                    return true;
            }
        }

        writerField = null!;
        return false;
    }

    /// <summary>
    /// Determines whether the reader recognises the writer's named type, comparing unqualified
    /// names and honouring the reader's aliases against the writer's full name (as Java does).
    /// </summary>
    private static bool SchemaNameEquals(NamedSchema reader, NamedSchema writer)
    {
        if (string.Equals(reader.Name, writer.Name, StringComparison.Ordinal))
            return true;

        return NamedSchemaAliases(reader).Contains(writer.Fullname, StringComparer.Ordinal);
    }

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

    /// <summary>An identity-based reader/writer pair, used to terminate recursion on recursive schemas.</summary>
    private readonly struct SchemaPair : IEquatable<SchemaPair>
    {
        private readonly Schema _reader;
        private readonly Schema _writer;

        public SchemaPair(Schema reader, Schema writer)
        {
            _reader = reader;
            _writer = writer;
        }

        public bool Equals(SchemaPair other) =>
            ReferenceEquals(_reader, other._reader) && ReferenceEquals(_writer, other._writer);

        public override bool Equals(object? obj) => obj is SchemaPair other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(RuntimeHelpers.GetHashCode(_reader), RuntimeHelpers.GetHashCode(_writer));
    }

    private static string Append(string location, string segment) =>
        location.EndsWith('/') ? location + segment : location + "/" + segment;

    private static string Append(string location, string first, string second) =>
        Append(Append(location, first), second);
}
