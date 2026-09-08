using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        [Description("Overwrite existing output files.")]
        [DefaultValue(false)]
        public bool Overwrite { get; set; }

        [CommandOption("-d|--output-dir")]
        [Description("Directory to save schemata.")]
        public DirectoryInfo? OutputDirectory { get; set; }
    }

    private readonly IStatusConsole _console;
    private readonly IStandardStreams _streams;
    private readonly IIdlToAvroTranslator _idlTranslator;

    public IdlToSchemataCommand(
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
            return InputValidation.ValidateStandardInputAlone(settings.IdlFiles, "IDL files");

        return InputValidation.Validate(settings.IdlFiles, "IDL");
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var outputDir = settings.OutputDirectory ?? new DirectoryInfo(Directory.GetCurrentDirectory());
        var directoryError = OutputCollector.EnsureDirectory(outputDir);
        if (directoryError != null)
        {
            _console.MarkupLineInterpolated($"[red]{directoryError}[/]");
            return ErrorCode.Error;
        }

        var collector = new OutputCollector(settings.Overwrite, OutputCollector.DetectPathComparer(outputDir));

        if (settings.FromStandardInput)
        {
            var content = await InputReader.TryReadAllTextAsync(_streams, true, null, InputSource.StandardInputName, _console, cancellationToken);
            if (content == null)
                return ErrorCode.Error;

            var baseDirectory = InputSource.ImportBaseDirectory(true, null);
            var ok = await ProcessAsync(content, InputSource.StandardInputName, baseDirectory, outputDir, collector, cancellationToken);
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
            var content = await InputReader.TryReadAllTextAsync(_streams, false, file, file, _console, cancellationToken);

            var ok = false;
            if (content != null)
            {
                var baseDirectory = InputSource.ImportBaseDirectory(false, file);
                ok = await ProcessAsync(content, file, baseDirectory, outputDir, collector, cancellationToken);
            }

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
        string baseDirectory,
        DirectoryInfo outputDir,
        OutputCollector collector,
        CancellationToken cancellationToken)
    {
        IdlParseResult parsed;
        try
        {
            parsed = await _idlTranslator.Translate(idlContent, baseDirectory, cancellationToken);
        }
        catch (Exception ex)
        {
            _console.MarkupLineInterpolated($"[red]Unable to parse IDL document '{source}': {ex.Message}[/]");
            return false;
        }

        try
        {
            var namedTypes = parsed.GetNamedTypesJson();

            // format output so it's human-readable
            var reservations = namedTypes
                .Select(s =>
                {
                    var fullName = IdlJson.GetFullName(s);
                    return new OutputReservation(
                        Path.Combine(outputDir.FullName, fullName + ".avsc"),
                        $"type '{fullName}'",
                        JsonFormatting.Indent(s.ToString()));
                })
                .ToList();

            var plan = collector.Reserve(reservations, source);
            if (plan.Error != null)
            {
                _console.MarkupLineInterpolated($"[red]Unable to generate schema files from '{source}': {plan.Error}[/]");
                return false;
            }

            await OutputCollector.WritePlanAsync(plan, _console, cancellationToken);

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
