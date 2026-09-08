using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using AvroProtocol = Avro.Protocol;
using AvroSchema = Avro.Schema;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// A convenience class used for enabling access to either a protocol or a schema
/// </summary>
public sealed record IdlParseResult
{
    private static readonly IReadOnlyDictionary<string, JObject> EmptyNamedSchemas = new Dictionary<string, JObject>();

    private readonly AvroProtocol? _protocol;
    private readonly AvroSchema? _schema;
    private readonly JToken _json;
    private readonly IReadOnlyDictionary<string, JObject> _namedSchemas;

    private IdlParseResult(AvroProtocol protocol, JToken json, IReadOnlyDictionary<string, JObject> namedSchemas)
    {
        _protocol = protocol;
        _json = json;
        _namedSchemas = namedSchemas;
    }

    private IdlParseResult(AvroSchema schema, JToken json, IReadOnlyDictionary<string, JObject> namedSchemas)
    {
        _schema = schema;
        _json = json;
        _namedSchemas = namedSchemas;
    }

    /// <summary>
    /// Constructs a parse result that contains a <see cref="AvroProtocol"/>, together with the raw
    /// JSON it was translated to. When <paramref name="json"/> is omitted, it is recovered from
    /// <paramref name="protocol"/> itself, which loses any property the Avro object model does not
    /// preserve when writing a protocol back out as JSON.
    /// </summary>
    public static IdlParseResult Protocol(AvroProtocol protocol, JObject? json = null, IReadOnlyDictionary<string, JObject>? namedSchemas = null)
    {
        ArgumentNullException.ThrowIfNull(protocol);

        return new(protocol, json ?? JObject.Parse(protocol.ToString()), namedSchemas ?? EmptyNamedSchemas);
    }

    /// <summary>
    /// Constructs a parse result that contains a <see cref="AvroSchema"/>, together with the raw
    /// JSON it was translated to. When <paramref name="json"/> is omitted, it is recovered from
    /// <paramref name="schema"/> itself, which loses any property the Avro object model does not
    /// preserve when writing a schema back out as JSON.
    /// </summary>
    public static IdlParseResult Schema(AvroSchema schema, JToken? json = null, IReadOnlyDictionary<string, JObject>? namedSchemas = null)
    {
        ArgumentNullException.ThrowIfNull(schema);

        return new(schema, json ?? JToken.Parse(schema.ToString()), namedSchemas ?? EmptyNamedSchemas);
    }

    /// <summary>
    /// Returns <c>true</c> when the value contains a <see cref="AvroProtocol"/>.
    /// </summary>
    public bool IsProtocol => _protocol != null;

    /// <summary>
    /// Returns <c>true</c> when the value contains a <see cref="AvroSchema"/>.
    /// </summary>
    public bool IsSchema => _schema != null;

    /// <summary>
    /// The document as translated, before being narrowed to a <see cref="AvroProtocol"/> or
    /// <see cref="AvroSchema"/>. Every property the source declared is present here, including ones
    /// the Avro object model itself does not round-trip.
    /// </summary>
    public JToken Json => _json;

    /// <summary>
    /// Enables actions to be performed depending on the contained value.
    /// </summary>
    public void Match(
        Action<AvroProtocol> protocol,
        Action<AvroSchema> schema)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        ArgumentNullException.ThrowIfNull(schema);

        if (IsProtocol)
            protocol.Invoke(_protocol!);

        if (IsSchema)
            schema.Invoke(_schema!);
    }

    /// <summary>
    /// Returns different values depending on the contained value.
    /// </summary>
    public T Match<T>(
        Func<AvroProtocol, T> protocol,
        Func<AvroSchema, T> schema)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        ArgumentNullException.ThrowIfNull(schema);

        return IsProtocol
            ? protocol.Invoke(_protocol!)
            : schema.Invoke(_schema!);
    }

    /// <summary>
    /// The named types (records, errors, enums and fixed types) reachable from this document, as
    /// self-contained raw JSON, deduplicated by full name in the order each is first reached: a
    /// protocol's declared types in turn, then any type each one reaches through its fields; or, for
    /// a schema, the types reachable from the schema itself.
    /// </summary>
    public IReadOnlyList<JObject> GetNamedTypesJson()
    {
        IEnumerable<JToken> roots = IsProtocol
            ? (IEnumerable<JToken>?)(((JObject)_json)["types"] as JArray) ?? []
            : [_json];

        return roots
            .SelectMany(root => IdlJson.GetNamedTypes(root, _namedSchemas))
            .DistinctBy(IdlJson.GetFullName, StringComparer.Ordinal)
            .Select(namedType => IdlJson.Inline(namedType, _namedSchemas))
            .ToList();
    }
}
