using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SJP.Avro.Tools;
using SJP.Avro.Tools.Idl;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvroTool.Commands;

internal sealed class IdlToSchemataCommand : AsyncCommand<IdlToSchemataCommand.Settings>
{
    private static readonly string[] IdlExtensions = [".avdl"];

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[IDL_FILES]")]
        [Description("One or more IDL files, directories or glob patterns to compile. Omit and use --stdin to read from standard input.")]
        public string[] IdlFiles { get; set; } = [];

        [CommandOption("--stdin")]
        [Description("Read the IDL from standard input instead of a file.")]
        [DefaultValue(false)]
        public bool FromStandardInput { get; set; }

        [CommandOption("--no-recursive")]
        [Description("When a directory is given, only process its top-level files instead of recursing.")]
        [DefaultValue(false)]
        public bool NoRecursive { get; set; }

        [CommandOption("--fail-fast")]
        [Description("Abort on the first input that fails instead of processing the rest.")]
        [DefaultValue(false)]
        public bool FailFast { get; set; }

        [CommandOption("-o|--overwrite")]
        [Description("Overwrite any existing types.")]
        [DefaultValue(false)]
        public bool Overwrite { get; set; }

        [CommandOption("-d|--output-dir")]
        [Description("Directory to save schemata.")]
        public DirectoryInfo? OutputDirectory { get; set; }
    }

    private readonly IAnsiConsole _console;
    private readonly IIdlToAvroTranslator _idlTranslator;

    public IdlToSchemataCommand(
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

        return InputValidation.Validate(settings.IdlFiles, "IDL");
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var outputDir = settings.OutputDirectory ?? new DirectoryInfo(Directory.GetCurrentDirectory());
        var collector = new OutputCollector(settings.Overwrite);

        if (settings.FromStandardInput)
        {
            var content = await InputSource.ReadAllTextAsync(true, null, cancellationToken);
            var ok = await ProcessAsync(content, "<stdin>", outputDir, collector, cancellationToken);
            return ok ? ErrorCode.Success : ErrorCode.Error;
        }

        var expansion = InputExpander.Expand(settings.IdlFiles, IdlExtensions, !settings.NoRecursive);
        foreach (var token in expansion.UnmatchedTokens)
            _console.MarkupLineInterpolated($"[red]No IDL files matched: {token}[/]");

        if (expansion.Files.Count == 0)
        {
            _console.MarkupLine("[red]No IDL files were found to compile.[/]");
            return ErrorCode.Error;
        }

        var anyFailed = expansion.UnmatchedTokens.Count > 0;
        foreach (var file in expansion.Files)
        {
            var content = await File.ReadAllTextAsync(file, cancellationToken);
            var ok = await ProcessAsync(content, file, outputDir, collector, cancellationToken);
            if (!ok)
            {
                anyFailed = true;
                if (settings.FailFast)
                    break;
            }
        }

        return anyFailed ? ErrorCode.Error : ErrorCode.Success;
    }

    private async Task<bool> ProcessAsync(
        string idlContent,
        string source,
        DirectoryInfo outputDir,
        OutputCollector collector,
        CancellationToken cancellationToken)
    {
        IdlFileParseResult parseResult;
        try
        {
            var result = await _idlTranslator.Translate(idlContent, cancellationToken);
            parseResult = IdlFileParseResult.Ok(result);
        }
        catch (Exception ex)
        {
            parseResult = IdlFileParseResult.Error(ex);
        }

        if (!parseResult.Success)
        {
            _console.MarkupLineInterpolated($"[red]Unable to parse IDL document '{source}': {parseResult.Exception.Message}[/]");
            return false;
        }

        try
        {
            var schemas = parseResult.Result.Match(
                p => p.Types,
                s => [s]);

            var namedTypes = schemas
                .SelectMany(s => s.GetNamedTypes())
                .ToList();

            var filenames = namedTypes
                .Select(s => Path.Combine(outputDir.FullName, s.Fullname + ".avsc"))
                .ToList();

            var reserveError = collector.Reserve(filenames, source);
            if (reserveError != null)
            {
                _console.MarkupLineInterpolated($"[red]Unable to generate schema files from '{source}': {reserveError}[/]");
                return false;
            }

            foreach (var namedType in namedTypes)
            {
                var namedTypeFilename = Path.Combine(outputDir.FullName, namedType.Fullname + ".avsc");

                // format output so it's human-readable
                var jsonNode = JsonNode.Parse(namedType.ToString());
                var formattedOutput = jsonNode!.ToJsonString(new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await OutputCollector.WriteAsync(namedTypeFilename, formattedOutput, cancellationToken);
                _console.MarkupLineInterpolated($"[green]Generated {namedTypeFilename}[/]");
            }

            return true;
        }
        catch (Exception ex)
        {
            _console.MarkupLineInterpolated($"[red]Failed to generate schema files from '{source}'.[/]");
            _console.MarkupLineInterpolated($"[red]    {ex.Message}[/]");

            return false;
        }
    }
}
