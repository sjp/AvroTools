using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avro;
using SJP.Avro.Tools.Idl;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvroTool.Commands;

internal sealed class CanonicalCommand : AsyncCommand<CanonicalCommand.Settings>
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
    }

    private readonly IStatusConsole _console;
    private readonly IStandardStreams _streams;
    private readonly IIdlToAvroTranslator _idlTranslator;

    public CanonicalCommand(
        IStatusConsole console,
        IStandardStreams streams,
        IIdlToAvroTranslator idlTranslator)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(streams);
        ArgumentNullException.ThrowIfNull(idlTranslator);

        _console = console;
        _streams = streams;
        _idlTranslator = idlTranslator;
    }

    protected override ValidationResult Validate(CommandContext context, Settings settings)
    {
        if (settings.FromStandardInput)
            return InputValidation.ValidateStandardInputAlone(settings.SchemaFile, "A schema file");

        if (string.IsNullOrWhiteSpace(settings.SchemaFile))
            return ValidationResult.Error("A schema file must be provided.");

        if (!File.Exists(settings.SchemaFile))
            return ValidationResult.Error($"A schema file could not be found at: {settings.SchemaFile}");

        return ValidationResult.Success();
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var displayName = settings.FromStandardInput ? InputSource.StandardInputName : settings.SchemaFile;
        var content = await InputReader.TryReadAllTextAsync(_streams, settings.FromStandardInput, settings.SchemaFile, displayName, _console, cancellationToken);
        if (content == null)
            return ErrorCode.Error;

        var baseDirectory = InputSource.ImportBaseDirectory(settings.FromStandardInput, settings.SchemaFile);
        var resolution = await AvroInputResolver.ResolveAsync(content, _idlTranslator, baseDirectory, cancellationToken);
        if (resolution.Input == null)
        {
            _console.MarkupLineInterpolated($"[red]{resolution.FailureMessage(displayName)}[/]");
            return ErrorCode.Error;
        }

        var input = resolution.Input;

        try
        {
            // Each top-level named type (the single schema, or each of a protocol's types) is
            // emitted as its own canonical form on its own line. The Parsing Canonical Form
            // embeds the fully-qualified name, so each line is self-describing.
            foreach (var schema in input.Schemas)
            {
                var canonicalForm = SchemaNormalization.ToParsingForm(schema);
                await _streams.Output.WriteLineAsync(canonicalForm.AsMemory(), cancellationToken);
            }

            return ErrorCode.Success;
        }
        catch (Exception ex)
        {
            _console.MarkupLine("[red]Failed to compute the canonical form.[/]");
            _console.MarkupLineInterpolated($"[red]    {ex.Message}[/]");

            return ErrorCode.Error;
        }
    }
}
