using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SJP.Avro.Tools.Idl;
using Spectre.Console;
using AvroProtocol = Avro.Protocol;
using AvroSchema = Avro.Schema;

namespace AvroTool;

/// <summary>
/// A parsed command input, resolved from one of an Avro IDL, JSON protocol or JSON schema: either
/// a protocol (from a JSON protocol or a protocol IDL) or a single schema (from a JSON schema or a
/// schema IDL).
/// </summary>
internal union AvroInput(AvroProtocol, AvroSchema)
{
    /// <summary>
    /// The top-level named schemas to operate on: the protocol's declared types, or the single
    /// schema.
    /// </summary>
    /// <remarks>
    /// A union cannot hold instance fields, so the list is built afresh on each read. Callers that
    /// need it more than once should keep it in a local.
    /// </remarks>
    public IReadOnlyList<AvroSchema> Schemas => this switch
    {
        AvroProtocol protocol => [.. protocol.Types],
        AvroSchema schema => [schema],
    };
}

/// <summary>
/// The reason textual input could not be parsed as any of the supported formats.
/// </summary>
/// <param name="Reason">Why the content could not be parsed, naming the parser whose failure is being reported.</param>
internal sealed record InputFailure(string Reason)
{
    /// <summary>
    /// Describes why an input could not be parsed, naming the input and the parser whose failure
    /// is being reported.
    /// </summary>
    /// <param name="inputName">The path the content was read from, or <c>&lt;stdin&gt;</c>.</param>
    public string Message(string inputName) => $"Input '{inputName}' {Reason}";
}

/// <summary>
/// The outcome of resolving textual input: either the parsed input, or the reason it could not
/// be parsed.
/// </summary>
internal union AvroInputResult(AvroInput, InputFailure)
{
    /// <summary>
    /// Separates the parsed input from the failure, so a caller can report the failure and stop
    /// with a single test.
    /// </summary>
    /// <param name="input">The parsed input, when the content could be parsed; otherwise the default value.</param>
    /// <param name="failure">The reason the content could not be parsed; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> when the content was parsed; otherwise <c>false</c>.</returns>
    public bool TryGetInput(out AvroInput input, [NotNullWhen(false)] out InputFailure? failure)
    {
        (input, failure) = this switch
        {
            AvroInput resolved => (resolved, null),
            InputFailure failed => (default(AvroInput), failed),
        };
        return failure == null;
    }
}

/// <summary>
/// Resolves textual input to an <see cref="AvroInput"/>, as a JSON protocol, a JSON schema or
/// an Avro IDL document, mirroring the detection used across the commands.
/// </summary>
/// <remarks>
/// The shape of the content chooses which parser to run first rather than each being tried in
/// turn: content that opens with a brace or a bracket is JSON, and a top-level <c>protocol</c>
/// property tells the two JSON parsers apart. A failed <c>Parse</c> costs a full Newtonsoft
/// parse and a thrown exception, so every IDL input used to pay for two of them before the
/// translator was reached, and every JSON schema for one. The remaining parsers are still tried
/// in their original order when the one the content was sniffed as does not accept it, so
/// anything that defies the sniff — a bare primitive name such as <c>"string"</c> is a valid
/// schema document that opens with neither — resolves exactly as it did before.
/// </remarks>
internal static class AvroInputResolver
{
    /// <summary>The top-level property that marks a JSON document as a protocol.</summary>
    private static ReadOnlySpan<byte> ProtocolPropertyName => "protocol"u8;

    /// <summary>
    /// Attempts to parse the given content as a protocol, schema or IDL document.
    /// </summary>
    /// <param name="content">The textual content to parse.</param>
    /// <param name="translator">The IDL translator to fall back to.</param>
    /// <param name="baseDirectory">The directory that relative IDL import paths are resolved against.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The resolved input, or the reason it could not be parsed as any of the three.</returns>
    public static async Task<AvroInputResult> ResolveAsync(string content, IIdlToAvroTranslator translator, string? baseDirectory, CancellationToken cancellationToken)
    {
        var isJson = LooksLikeJson(content);
        var declaresProtocol = isJson && DeclaresProtocol(content);

        string? protocolError = null;
        string? schemaError = null;

        if (isJson)
        {
            if (declaresProtocol)
            {
                if (TryParseProtocol(content, out var protocol, out protocolError))
                    return new AvroInput(protocol);

                if (TryParseSchema(content, out var schema, out schemaError))
                    return new AvroInput(schema);
            }
            else
            {
                if (TryParseSchema(content, out var schema, out schemaError))
                    return new AvroInput(schema);

                if (TryParseProtocol(content, out var protocol, out protocolError))
                    return new AvroInput(protocol);
            }
        }

        string idlError;
        try
        {
            var result = await translator.Translate(content, baseDirectory, cancellationToken);
            return result.Document switch
            {
                AvroProtocol protocol => new AvroInput(protocol),
                AvroSchema schema => new AvroInput(schema),
            };
        }
        catch (Exception ex)
        {
            idlError = ex.Message;
        }

        if (!isJson)
        {
            if (TryParseProtocol(content, out var protocol, out protocolError))
                return new AvroInput(protocol);

            if (TryParseSchema(content, out var schema, out schemaError))
                return new AvroInput(schema);
        }

        return new InputFailure(DescribeFailure(isJson, declaresProtocol, protocolError!, schemaError!, idlError));
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
        IStatusConsole console,
        CancellationToken cancellationToken)
    {
        var fromStandardInput = schemaFile == null;
        var displayName = fromStandardInput ? InputSource.StandardInputName : schemaFile!;

        var content = await InputReader.TryReadAllTextAsync(streams, fromStandardInput, schemaFile, displayName, console, cancellationToken);
        if (content == null)
            return null;

        var baseDirectory = InputSource.ImportBaseDirectory(fromStandardInput, schemaFile);
        var resolution = await ResolveAsync(content, translator, baseDirectory, cancellationToken);
        if (!resolution.TryGetInput(out var input, out var failure))
        {
            console.MarkupLineInterpolated($"[red]{failure.Message(displayName)}[/]");
            return null;
        }

        var schemas = input.Schemas;
        if (schemas.Count != 1)
        {
            console.MarkupLineInterpolated($"[red]Input '{displayName}' resolves to {schemas.Count} named types; {commandName} expects a single schema per input.[/]");
            return null;
        }

        return new SchemaSource(displayName, schemas[0]);
    }

    /// <summary>
    /// Chooses which of the three failures to report, based on the shape of the content: JSON
    /// (an object or array) is judged against the protocol parser when it declares a protocol
    /// and the schema parser otherwise, and anything else against the IDL parser. Reporting the
    /// one parser the content was plainly meant for keeps the real cause visible, instead of
    /// three unrelated messages or none at all.
    /// </summary>
    /// <remarks>
    /// These are the same two questions the parser order was chosen by, so the answers are
    /// passed in rather than asked again of content that is by now known to be unusable.
    /// </remarks>
    private static string DescribeFailure(bool isJson, bool declaresProtocol, string protocolError, string schemaError, string idlError)
    {
        if (!isJson)
            return $"could not be parsed as Avro IDL: {idlError}";

        return declaresProtocol
            ? $"could not be parsed as a JSON protocol: {protocolError}"
            : $"could not be parsed as a JSON schema: {schemaError}";
    }

    /// <summary>
    /// Whether the content opens as a JSON object or array. Content that does not is Avro IDL,
    /// or too malformed for any parser to make sense of.
    /// </summary>
    private static bool LooksLikeJson(string content)
    {
        var start = content.AsSpan().TrimStart();
        return start.Length > 0 && (start[0] == '{' || start[0] == '[');
    }

    /// <summary>
    /// Whether the content is a JSON object carrying a <c>protocol</c> key, the property that
    /// distinguishes a protocol document from a schema one. Content too malformed to read as
    /// JSON at all is treated as a schema, the more common of the two.
    /// </summary>
    /// <remarks>
    /// Only the top-level property names are of interest, so the document is scanned with a
    /// reader and its values skipped rather than materialised into a node tree: the answer is
    /// wanted before either parser has run, and building a whole second document to find one
    /// key would cost more than the parse it saves.
    /// </remarks>
    private static bool DeclaresProtocol(string content)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetByteCount(content));
        try
        {
            var length = Encoding.UTF8.GetBytes(content, buffer);
            var reader = new Utf8JsonReader(buffer.AsSpan(0, length));

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return false;

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals(ProtocolPropertyName))
                    return true;

                reader.Skip();
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool TryParseProtocol(string content, out AvroProtocol protocol, out string error)
    {
        try
        {
            protocol = AvroProtocol.Parse(content);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            protocol = default!;
            error = ex.Message;
            return false;
        }
    }

    private static bool TryParseSchema(string content, out AvroSchema schema, out string error)
    {
        try
        {
            schema = AvroSchema.Parse(content);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            schema = default!;
            error = ex.Message;
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
