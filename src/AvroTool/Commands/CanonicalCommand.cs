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

    private readonly IAnsiConsole _console;
    private readonly IIdlToAvroTranslator _idlTranslator;

    public CanonicalCommand(
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

        try
        {
            // Each top-level named type (the single schema, or each of a protocol's types) is
            // emitted as its own canonical form on its own line. The Parsing Canonical Form
            // embeds the fully-qualified name, so each line is self-describing.
            foreach (var schema in input.Schemas)
            {
                var canonicalForm = SchemaNormalization.ToParsingForm(schema);
                await Console.Out.WriteLineAsync(canonicalForm.AsMemory(), cancellationToken);
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
