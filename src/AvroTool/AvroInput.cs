using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SJP.Avro.Tools.Idl;
using AvroProtocol = Avro.Protocol;
using AvroSchema = Avro.Schema;

namespace AvroTool;

/// <summary>
/// A parsed command input, resolved from one of an Avro IDL, JSON protocol or JSON schema.
/// </summary>
internal sealed class AvroInput
{
    private AvroInput(AvroProtocol? protocol, AvroSchema? schema)
    {
        Protocol = protocol;
        Schema = schema;
    }

    /// <summary>The parsed protocol, when the input was a protocol (or protocol IDL); otherwise <c>null</c>.</summary>
    public AvroProtocol? Protocol { get; }

    /// <summary>The parsed schema, when the input was a single schema (or schema IDL); otherwise <c>null</c>.</summary>
    public AvroSchema? Schema { get; }

    /// <summary>
    /// The top-level named schemas to operate on: the protocol's declared types, or the single schema.
    /// </summary>
    public IReadOnlyList<AvroSchema> Schemas => Protocol != null
        ? [.. Protocol.Types]
        : [Schema!];

    public static AvroInput FromProtocol(AvroProtocol protocol) => new(protocol, null);

    public static AvroInput FromSchema(AvroSchema schema) => new(null, schema);
}

/// <summary>
/// Resolves textual input to an <see cref="AvroInput"/>, trying JSON protocol, then JSON
/// schema, then Avro IDL (in that order), mirroring the detection used across the commands.
/// </summary>
internal static class AvroInputResolver
{
    /// <summary>
    /// Attempts to parse the given content as a protocol, schema or IDL document.
    /// </summary>
    /// <returns>The resolved input, or <c>null</c> if it could not be parsed as any of the three.</returns>
    public static async Task<AvroInput?> ResolveAsync(string content, IIdlToAvroTranslator translator, CancellationToken cancellationToken)
    {
        if (TryParseProtocol(content, out var protocol))
            return AvroInput.FromProtocol(protocol);

        if (TryParseSchema(content, out var schema))
            return AvroInput.FromSchema(schema);

        try
        {
            var result = await translator.Translate(content, cancellationToken);
            return result.Match<AvroInput>(AvroInput.FromProtocol, AvroInput.FromSchema);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseProtocol(string content, out AvroProtocol protocol)
    {
        try
        {
            protocol = AvroProtocol.Parse(content);
            return true;
        }
        catch
        {
            protocol = default!;
            return false;
        }
    }

    private static bool TryParseSchema(string content, out AvroSchema schema)
    {
        try
        {
            schema = AvroSchema.Parse(content);
            return true;
        }
        catch
        {
            schema = default!;
            return false;
        }
    }
}
