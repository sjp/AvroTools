using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SJP.Avro.Tools.Idl;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvroTool.Commands;

internal sealed class IdlToSchemataCommand : AsyncCommand<IdlToSchemataCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<IDL_FILE>")]
        [Description("An IDL file to compile.")]
        public string IdlFile { get; set; } = string.Empty;

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

    public override ValidationResult Validate(CommandContext context, Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.IdlFile))
            return ValidationResult.Error("An IDL file must be provided.");

        if (!File.Exists(settings.IdlFile))
            return ValidationResult.Error($"An IDL file could not be found at: {settings.IdlFile}");

        return ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var parseResult = await ParseIdl(settings.IdlFile, cancellationToken);
        if (!parseResult.Success)
        {
            _console.MarkupLineInterpolated($"[red]Unable to parse IDL document: {parseResult.Exception.Message}[/]");
            return ErrorCode.Error;
        }

        var outputDir = settings.OutputDirectory ?? new DirectoryInfo(Directory.GetCurrentDirectory());

        try
        {
            var avroSchemas = parseResult.Result.Match(
                p => p.Types,
                s => [s]);

            var filenames = avroSchemas
                .Select(s => Path.Combine(outputDir.FullName, s.Name + ".avsc"))
                .ToList();

            var existingFiles = filenames.Where(File.Exists).ToList();
            if (existingFiles.Count > 0 && !settings.Overwrite)
            {
                _console.MarkupLine("[red]Unable to generate schema files. One or more files exist.[/]");
                foreach (var existingFile in existingFiles)
                    _console.MarkupLineInterpolated($"[red]    {existingFile}[/]");
                return ErrorCode.Error;
            }

            foreach (var schema in avroSchemas)
            {
                var schemaFilename = Path.Combine(outputDir.FullName, schema.Name + ".avsc");
                if (File.Exists(schemaFilename))
                    File.Delete(schemaFilename);

                // format output so it's human-readable
                var jsonNode = JsonNode.Parse(schema.ToString());
                var formattedOutput = jsonNode!.ToJsonString(new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(schemaFilename, formattedOutput, cancellationToken);
                _console.MarkupLineInterpolated($"[green]Generated {schemaFilename}[/]");
            }

            return ErrorCode.Success;
        }
        catch (Exception ex)
        {
            _console.MarkupLine("[red]Failed to generate schema files.[/]");
            _console.MarkupLineInterpolated($"[red]    {ex.Message}[/]");

            return ErrorCode.Error;
        }
    }

    private async Task<IdlFileParseResult> ParseIdl(string idlFile, CancellationToken cancellationToken)
    {
        try
        {
            await using var idlFileStream = File.OpenRead(idlFile);
            var result = await _idlTranslator.Translate(idlFileStream, cancellationToken);
            return IdlFileParseResult.Ok(result);
        }
        catch (Exception ex)
        {
            return IdlFileParseResult.Error(ex);
        }
    }
}