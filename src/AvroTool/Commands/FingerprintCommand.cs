using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avro;
using SJP.Avro.Tools.Idl;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvroTool.Commands;

internal sealed class FingerprintCommand : AsyncCommand<FingerprintCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[SCHEMA_FILE]")]
        [Description("An IDL, protocol or schema file. Omit and use --stdin to read from standard input.")]
        public string SchemaFile { get; set; } = string.Empty;

        [CommandOption("--stdin")]
        [Description("Read the input from standard input instead of a file.")]
        [DefaultValue(false)]
        public bool FromStandardInput { get; set; }

        [CommandOption("-a|--algorithm")]
        [Description("The fingerprint algorithm: crc-64-avro (default), md5 or sha-256.")]
        [DefaultValue("crc-64-avro")]
        public string Algorithm { get; set; } = "crc-64-avro";

        [CommandOption("-f|--format")]
        [Description("The output format: hex (default), base64 or long. 'long' applies only to crc-64-avro.")]
        [DefaultValue("hex")]
        public string Format { get; set; } = "hex";
    }

    // The algorithm names understood by Apache.Avro's SchemaNormalization, keyed by a normalised
    // (lower-case, hyphen-stripped) form of the user-supplied value.
    private static readonly System.Collections.Generic.Dictionary<string, string> Algorithms = new()
    {
        ["crc64avro"] = "CRC-64-AVRO",
        ["crc64"] = "CRC-64-AVRO",
        ["rabin"] = "CRC-64-AVRO",
        ["md5"] = "MD5",
        ["sha256"] = "SHA-256",
    };

    private const string HexFormat = "hex";
    private const string Base64Format = "base64";
    private const string LongFormat = "long";

    private readonly IAnsiConsole _console;
    private readonly IIdlToAvroTranslator _idlTranslator;

    public FingerprintCommand(
        IAnsiConsole console,
        IIdlToAvroTranslator idlTranslator)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(idlTranslator);

        _console = console;
        _idlTranslator = idlTranslator;
    }

    protected override ValidationResult Validate(CommandContext context, Settings settings)
    {
        if (!Algorithms.ContainsKey(Normalise(settings.Algorithm)))
            return ValidationResult.Error($"Unknown algorithm '{settings.Algorithm}'. Supported: crc-64-avro, md5, sha-256.");

        var format = Normalise(settings.Format);
        if (format is not (HexFormat or Base64Format or LongFormat))
            return ValidationResult.Error($"Unknown format '{settings.Format}'. Supported: hex, base64, long.");

        if (format == LongFormat && Algorithms[Normalise(settings.Algorithm)] != "CRC-64-AVRO")
            return ValidationResult.Error("The 'long' format is only valid for the crc-64-avro algorithm.");

        if (settings.FromStandardInput)
            return ValidationResult.Success();

        if (string.IsNullOrWhiteSpace(settings.SchemaFile))
            return ValidationResult.Error("A schema file must be provided.");

        if (!File.Exists(settings.SchemaFile))
            return ValidationResult.Error($"A schema file could not be found at: {settings.SchemaFile}");

        return ValidationResult.Success();
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var content = await InputSource.ReadAllTextAsync(settings.FromStandardInput, settings.SchemaFile, cancellationToken);
        var input = await AvroInputResolver.ResolveAsync(content, _idlTranslator, cancellationToken);
        if (input == null)
        {
            _console.MarkupLine("[red]Input unable to be parsed as one of Avro IDL, JSON protocol or JSON schema.[/]");
            return ErrorCode.Error;
        }

        var avroAlgorithm = Algorithms[Normalise(settings.Algorithm)];
        var format = Normalise(settings.Format);

        try
        {
            var schemas = input.Schemas;
            var labelled = schemas.Count > 1;

            foreach (var schema in schemas)
            {
                var value = FormatFingerprint(schema, avroAlgorithm, format);

                // With more than one schema (a protocol's types), label each value with the
                // schema's full name, in the style of the *sum tools ("<value>  <name>").
                var line = labelled ? $"{value}  {schema.Fullname}" : value;
                await Console.Out.WriteLineAsync(line.AsMemory(), cancellationToken);
            }

            return ErrorCode.Success;
        }
        catch (Exception ex)
        {
            _console.MarkupLine("[red]Failed to compute the fingerprint.[/]");
            _console.MarkupLineInterpolated($"[red]    {ex.Message}[/]");

            return ErrorCode.Error;
        }
    }

    private static string FormatFingerprint(Schema schema, string avroAlgorithm, string format)
    {
        if (format == LongFormat)
            return SchemaNormalization.ParsingFingerprint64(schema).ToString(CultureInfo.InvariantCulture);

        var bytes = SchemaNormalization.ParsingFingerprint(avroAlgorithm, schema);
        return format == Base64Format
            ? Convert.ToBase64String(bytes)
            : Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Normalise(string value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant().Replace("-", string.Empty);
}
