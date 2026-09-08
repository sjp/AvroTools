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
    /// A result that rests on that assumption being made about a pair other than itself is not
    /// memoised, because it is only the answer for positions beneath that other pair; reached
    /// from anywhere else, the pair is walked again and reports everything it finds there.
    /// Memoised incompatibilities carry locations relative to the pair they were found in, so a
    /// type reached from several places reports the same findings once at each of those places,
    /// each with its own path.
    /// </summary>
    private sealed class Checker
    {
        private readonly Dictionary<SchemaPair, List<Incompatibility>> _memo = [];

        /// <summary>The pairs currently being computed, each mapped to its depth on the stack.</summary>
        private readonly Dictionary<SchemaPair, int> _inFlight = [];

        /// <summary>
        /// The shallowest in-flight pair the computation in progress has assumed compatible,
        /// or <see cref="int.MaxValue"/> when it has assumed nothing. A result that rests on an
        /// assumption about a pair further up the stack is only valid beneath that pair, so it
        /// must not be memoised: reached from elsewhere, the same pair can have more to report.
        /// </summary>
        private int _assumedDepth = int.MaxValue;

        private static readonly List<Incompatibility> None = [];

        public List<Incompatibility> Calculate(Schema reader, Schema writer, string location) =>
            Rebase(CalculateRelative(reader, writer), location);

        /// <summary>
        /// Computes the incompatibilities of a pair with locations relative to that pair's own
        /// root, which is what makes the memoised result reusable from any position.
        /// </summary>
        private List<Incompatibility> CalculateRelative(Schema reader, Schema writer)
        {
            // Logical types resolve on their underlying representation, so compare the base schemas.
            reader = Unwrap(reader);
            writer = Unwrap(writer);

            var pair = new SchemaPair(reader, writer);
            if (_memo.TryGetValue(pair, out var cached))
                return cached;

            if (_inFlight.TryGetValue(pair, out var inFlightDepth))
            {
                // Recursion: assume the pair compatible, and record what that assumption rests on.
                _assumedDepth = Math.Min(_assumedDepth, inFlightDepth);
                return None;
            }

            var depth = _inFlight.Count;
            _inFlight[pair] = depth;

            var callerAssumedDepth = _assumedDepth;
            _assumedDepth = int.MaxValue;

            var sink = new List<Incompatibility>();
            Compute(sink, reader, writer, RelativeRoot);

            var assumedDepth = _assumedDepth;
            _inFlight.Remove(pair);

            // An assumption about this pair itself is resolved here: the result is the fixed point
            // for a type that is compatible with itself, and holds wherever the pair is reached.
            if (assumedDepth >= depth)
            {
                _memo[pair] = sink;
                _assumedDepth = callerAssumedDepth;
            }
            else
            {
                _assumedDepth = Math.Min(callerAssumedDepth, assumedDepth);
            }

            return sink;
        }

        private static List<Incompatibility> Rebase(List<Incompatibility> relative, string location)
        {
            var rebased = new List<Incompatibility>(relative.Count);
            foreach (var incompatibility in relative)
            {
                rebased.Add(new Incompatibility(
                    incompatibility.Type,
                    incompatibility.Message,
                    Combine(location, incompatibility.Location)));
            }

            return rebased;
        }

        private void Compute(List<Incompatibility> sink, Schema reader, Schema writer, string location)
        {
            var readerType = ResolutionType(reader);
            var writerType = ResolutionType(writer);

            if (readerType == writerType)
            {
                ComputeSameType(sink, readerType, reader, writer, location);
                return;
            }

            // A writer union is readable when every branch it can emit is readable by the reader.
            if (writerType == Schema.Type.Union)
            {
                foreach (var writerBranch in ((UnionSchema)writer).Schemas)
                    sink.AddRange(Calculate(reader, writerBranch, location));
                return;
            }

            // A reader union reads a non-union writer if any branch can read it.
            if (readerType == Schema.Type.Union)
            {
                CheckReaderUnion(sink, (UnionSchema)reader, writer, location);
                return;
            }

            if (!IsPromotable(readerType, writerType))
                AddTypeMismatch(sink, reader, writer, location);
        }

        private void ComputeSameType(List<Incompatibility> sink, Schema.Type type, Schema reader, Schema writer, string location)
        {
            switch (type)
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
            var readable = reader.Schemas.Any(branch => CalculateRelative(branch, writer).Count == 0);
            if (!readable)
            {
                sink.Add(new Incompatibility(
                    SchemaIncompatibilityType.MissingUnionBranch,
                    $"reader union lacking writer type: {Describe(writer)}",
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
                    sink.AddRange(Calculate(readerField.Schema, writerField.Schema, Append(location, "fields", readerField.Name, "type")));
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
                $"reader type: {TypeName(reader)} not compatible with writer type: {TypeName(writer)}",
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
    /// The name the Avro specification gives a schema's type (<c>int</c>, <c>enum</c>, …), which is
    /// what a schema document is written in and therefore what a reader of a report expects to see.
    /// </summary>
    private static string TypeName(Schema schema) => Schema.GetTypeString(schema.Tag);

    /// <summary>
    /// Describes a schema for a message: its type, qualified by its full name when it is a named
    /// type, because for those the name is usually the reason a resolution failed.
    /// </summary>
    private static string Describe(Schema schema) =>
        schema is NamedSchema named
            ? TypeName(schema) + " " + named.Fullname
            : TypeName(schema);

    /// <summary>
    /// The type a schema is resolved as. A protocol error is a record that carries an error flag:
    /// it declares fields the same way and resolves against a record field by field, so both are
    /// resolved as <see cref="Schema.Type.Record"/>, and a type that changes between the two is
    /// not by itself a mismatch.
    /// </summary>
    private static Schema.Type ResolutionType(Schema schema) =>
        schema.Tag == Schema.Type.Error ? Schema.Type.Record : schema.Tag;

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

    /// <summary>The location of the pair currently being computed, which every finding hangs off.</summary>
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

    private static string Append(string location, string first, string second, string third) =>
        Append(Append(Append(location, first), second), third);
}
