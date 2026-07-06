using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SJP.Avro.Tools.Idl;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvroTool.Commands;

internal sealed class IdlCommand : AsyncCommand<IdlCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[IDL_FILE]")]
        [Description("An IDL file to compile. Omit and use --stdin to read from standard input.")]
        public string IdlFile { get; set; } = string.Empty;

        [CommandOption("--stdin")]
        [Description("Read the IDL from standard input instead of a file.")]
        [DefaultValue(false)]
        public bool FromStandardInput { get; set; }

        [CommandOption("-o|--overwrite")]
        [Description("Overwrite any existing types.")]
        [DefaultValue(false)]
        public bool Overwrite { get; set; }

        [CommandOption("-d|--output-dir")]
        [Description("Directory to save generated JSON.")]
        public DirectoryInfo? OutputDirectory { get; set; }

        [CommandOption("-s|--stdout")]
        [Description("Write the generated JSON to standard output instead of a file.")]
        [DefaultValue(false)]
        public bool ToStandardOutput { get; set; }
    }

    private readonly IAnsiConsole _console;
    private readonly IIdlToAvroTranslator _idlTranslator;

    public IdlCommand(
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

        if (string.IsNullOrWhiteSpace(settings.IdlFile))
            return ValidationResult.Error("An IDL file must be provided.");

        if (!File.Exists(settings.IdlFile))
            return ValidationResult.Error($"An IDL file could not be found at: {settings.IdlFile}");

        return ValidationResult.Success();
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var idlContent = await InputSource.ReadAllTextAsync(settings.FromStandardInput, settings.IdlFile, cancellationToken);
        var parseResult = await ParseIdl(idlContent, cancellationToken);
        if (!parseResult.Success)
        {
            _console.MarkupLineInterpolated($"[red]Unable to parse IDL document: {parseResult.Exception.Message}[/]");
            return ErrorCode.Error;
        }

        var avroOutputType = parseResult.Result.Match(
            _ => "protocol",
            _ => "schema");

        try
        {
            var output = parseResult.Result.Match(
                p => p.ToString(),
                s => s.ToString());

            // format output so it's human-readable
            var jsonNode = JsonNode.Parse(output);
            var formattedOutput = jsonNode!.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            });

            if (settings.ToStandardOutput)
            {
                await Console.Out.WriteLineAsync(formattedOutput.AsMemory(), cancellationToken);
                return ErrorCode.Success;
            }

            var outputDir = settings.OutputDirectory ?? new DirectoryInfo(Directory.GetCurrentDirectory());
            var outputFileName = parseResult.Result.Match(
                p => p.Name + ".avpr",
                s => s.Name + ".avsc");
            var outputPath = Path.Combine(outputDir.FullName, outputFileName);

            if (File.Exists(outputPath) && !settings.Overwrite)
            {
                _console.MarkupLineInterpolated($"[red]The output file path '{outputPath}' cannot be used[/]");
                _console.MarkupLine("[red]A file already exists. Consider using the 'overwrite' option.[/]");
                return ErrorCode.Error;
            }

            if (File.Exists(outputPath))
                File.Delete(outputPath);

            await File.WriteAllTextAsync(outputPath, formattedOutput, cancellationToken);

            _console.MarkupLineInterpolated($"[green]Generated {outputPath}[/]");

            return ErrorCode.Success;
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[red]Failed to generate Avro {avroOutputType} file.[/]");
            _console.MarkupLineInterpolated($"[red]    {ex.Message}[/]");

            return ErrorCode.Error;
        }
    }

    private async Task<IdlFileParseResult> ParseIdl(string idlContent, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _idlTranslator.Translate(idlContent, cancellationToken);
            return IdlFileParseResult.Ok(result);
        }
        catch (Exception ex)
        {
            return IdlFileParseResult.Error(ex);
        }
    }
}