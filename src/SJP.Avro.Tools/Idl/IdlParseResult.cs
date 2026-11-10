using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using AvroProtocol = Avro.Protocol;
using AvroSchema = Avro.Schema;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// The outcome of translating an IDL document: the <see cref="AvroDocument"/> it translated to,
/// either a protocol or a schema, together with its raw JSON and any warnings.
/// </summary>
public sealed record IdlParseResult
{
    private static readonly IReadOnlyDictionary<string, JObject> EmptyNamedSchemas = FrozenDictionary<string, JObject>.Empty;

    private readonly JToken _json;
    private readonly IReadOnlyDictionary<string, JObject> _namedSchemas;

    private IdlParseResult(AvroDocument document, JToken json, IReadOnlyDictionary<string, JObject> namedSchemas, IReadOnlyList<string> warnings)
    {
        Document = document;
        _json = json;
        _namedSchemas = namedSchemas;
        Warnings = warnings;
    }

    /// <summary>
    /// Constructs a parse result that contains a <see cref="AvroProtocol"/>, together with the raw
    /// JSON it was translated to. When <paramref name="json"/> is omitted, it is recovered from
    /// <paramref name="protocol"/> itself, which loses any property the Avro object model does not
    /// preserve when writing a protocol back out as JSON.
    /// </summary>
    public static IdlParseResult Protocol(AvroProtocol protocol, JObject? json = null, IReadOnlyDictionary<string, JObject>? namedSchemas = null, IReadOnlyList<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(protocol);

        return new(protocol, json ?? JObject.Parse(protocol.ToString()), namedSchemas ?? EmptyNamedSchemas, warnings ?? []);
    }

    /// <summary>
    /// Constructs a parse result that contains a <see cref="AvroSchema"/>, together with the raw
    /// JSON it was translated to. When <paramref name="json"/> is omitted, it is recovered from
    /// <paramref name="schema"/> itself, which loses any property the Avro object model does not
    /// preserve when writing a schema back out as JSON.
    /// </summary>
    public static IdlParseResult Schema(AvroSchema schema, JToken? json = null, IReadOnlyDictionary<string, JObject>? namedSchemas = null, IReadOnlyList<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(schema);

        return new(schema, json ?? JToken.Parse(schema.ToString()), namedSchemas ?? EmptyNamedSchemas, warnings ?? []);
    }

    /// <summary>
    /// The protocol or schema the IDL document translated to. Inspect it with pattern matching,
    /// such as a <c>switch</c> expression over <see cref="AvroProtocol"/> and
    /// <see cref="AvroSchema"/>.
    /// </summary>
    public AvroDocument Document { get; }

    /// <summary>
    /// Returns <c>true</c> when the value contains a <see cref="AvroProtocol"/>.
    /// </summary>
    [Obsolete("Test the union instead: use Document with pattern matching, such as 'result.Document is Avro.Protocol'.")]
    public bool IsProtocol => Document is AvroProtocol;

    /// <summary>
    /// Returns <c>true</c> when the value contains a <see cref="AvroSchema"/>.
    /// </summary>
    [Obsolete("Test the union instead: use Document with pattern matching, such as 'result.Document is Avro.Schema'.")]
    public bool IsSchema => Document is AvroSchema;

    /// <summary>
    /// The document as translated, before being narrowed to a <see cref="AvroProtocol"/> or
    /// <see cref="AvroSchema"/>. Every property the source declared is present here, including ones
    /// the Avro object model itself does not round-trip.
    /// </summary>
    public JToken Json => _json;

    /// <summary>
    /// Describes anything about the document that is worth reporting but did not stop it being
    /// translated, such as a doc comment written where no declaration can claim it. A warning
    /// raised by an imported document names the import it came from.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>
    /// Invokes whichever of the two actions matches the contained value. Exactly one of them is
    /// invoked.
    /// </summary>
    /// <param name="protocol">Invoked with the protocol, when the value contains one.</param>
    /// <param name="schema">Invoked with the schema, when the value contains one.</param>
    [Obsolete("Use Document with pattern matching instead, such as a switch statement over Avro.Protocol and Avro.Schema.")]
    public void Match(
        Action<AvroProtocol> protocol,
        Action<AvroSchema> schema)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        ArgumentNullException.ThrowIfNull(schema);

        switch (Document)
        {
            case AvroProtocol p:
                protocol.Invoke(p);
                break;
            case AvroSchema s:
                schema.Invoke(s);
                break;
        }
    }

    /// <summary>
    /// Returns the result of whichever of the two functions matches the contained value.
    /// Exactly one of them is invoked.
    /// </summary>
    /// <typeparam name="T">The type of value returned.</typeparam>
    /// <param name="protocol">Invoked with the protocol, when the value contains one.</param>
    /// <param name="schema">Invoked with the schema, when the value contains one.</param>
    /// <returns>The value returned by the function that was invoked.</returns>
    [Obsolete("Use Document with pattern matching instead, such as a switch expression over Avro.Protocol and Avro.Schema.")]
    public T Match<T>(
        Func<AvroProtocol, T> protocol,
        Func<AvroSchema, T> schema)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        ArgumentNullException.ThrowIfNull(schema);

        return Document switch
        {
            AvroProtocol p => protocol.Invoke(p),
            AvroSchema s => schema.Invoke(s),
        };
    }

    /// <summary>
    /// The named types (records, errors, enums and fixed types) reachable from this document, as
    /// self-contained raw JSON, deduplicated by full name in the order each is first reached: a
    /// protocol's declared types in turn, then any type each one reaches through its fields; or, for
    /// a schema, the types reachable from the schema itself.
    /// </summary>
    public IReadOnlyList<JObject> GetNamedTypesJson()
    {
        IEnumerable<JToken> roots = Document is AvroProtocol
            ? (IEnumerable<JToken>?)(((JObject)_json)["types"] as JArray) ?? []
            : [_json];

        return IdlJson.GetNamedTypes(roots, _namedSchemas)
            .Select(namedType => IdlJson.Inline(namedType, _namedSchemas))
            .ToList();
    }
}
