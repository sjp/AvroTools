using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SJP.Avro.Tools.Idl;
using Spectre.Console;
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
    /// <param name="content">The textual content to parse.</param>
    /// <param name="translator">The IDL translator to fall back to.</param>
    /// <param name="baseDirectory">The directory that relative IDL import paths are resolved against.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The resolved input, or <c>null</c> if it could not be parsed as any of the three.</returns>
    public static async Task<AvroInput?> ResolveAsync(string content, IIdlToAvroTranslator translator, string? baseDirectory, CancellationToken cancellationToken)
    {
        if (TryParseProtocol(content, out var protocol))
            return AvroInput.FromProtocol(protocol);

        if (TryParseSchema(content, out var schema))
            return AvroInput.FromSchema(schema);

        try
        {
            var result = await translator.Translate(content, baseDirectory, cancellationToken);
            return result.Match<AvroInput>(AvroInput.FromProtocol, AvroInput.FromSchema);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a file, or standard input, and resolves it to the single named schema the caller
    /// requires, reporting any failure to the console.
    /// </summary>
    /// <param name="schemaFile">The path of the file to read, or <c>null</c> to read standard input.</param>
    /// <param name="streams">The standard streams to read standard input from.</param>
    /// <param name="translator">The IDL translator to fall back to.</param>
    /// <param name="commandName">The command name to use when reporting an unusable input.</param>
    /// <param name="console">The console to write failures to.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The resolved schema, or <c>null</c> when the input could not be used.</returns>
    public static async Task<SchemaSource?> ResolveSingleSchemaAsync(
        string? schemaFile,
        IStandardStreams streams,
        IIdlToAvroTranslator translator,
        string commandName,
        IAnsiConsole console,
        CancellationToken cancellationToken)
    {
        var fromStandardInput = schemaFile == null;
        var displayName = fromStandardInput ? InputSource.StandardInputName : schemaFile!;

        var content = await streams.ReadAllTextAsync(fromStandardInput, schemaFile, cancellationToken);
        var baseDirectory = InputSource.ImportBaseDirectory(fromStandardInput, schemaFile);
        var input = await ResolveAsync(content, translator, baseDirectory, cancellationToken);
        if (input == null)
        {
            console.MarkupLineInterpolated($"[red]Input '{displayName}' unable to be parsed as one of Avro IDL, JSON protocol or JSON schema.[/]");
            return null;
        }

        if (input.Schemas.Count != 1)
        {
            console.MarkupLineInterpolated($"[red]Input '{displayName}' resolves to {input.Schemas.Count} named types; {commandName} expects a single schema per input.[/]");
            return null;
        }

        return new SchemaSource(displayName, input.Schemas[0]);
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

/// <summary>
/// A single schema, paired with the input path it was resolved from.
/// </summary>
/// <param name="Source">The path the schema was read from, or <c>&lt;stdin&gt;</c> for standard input.</param>
/// <param name="Schema">The resolved schema.</param>
internal sealed record SchemaSource(string Source, AvroSchema Schema);
