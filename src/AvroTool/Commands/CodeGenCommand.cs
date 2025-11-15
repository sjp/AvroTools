using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avro;
using SJP.Avro.Tools;
using SJP.Avro.Tools.CodeGen;
using SJP.Avro.Tools.Idl;
using Spectre.Console;
using Spectre.Console.Cli;
using Superpower;
using AvroProtocol = Avro.Protocol;
using AvroSchema = Avro.Schema;

namespace AvroTool.Commands;

internal sealed class CodeGenCommand : AsyncCommand<CodeGenCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<INPUT_FILE>")]
        [Description("An IDL, protocol or schema file to generate C# code from.")]
        public string InputFile { get; set; } = string.Empty;

        [CommandArgument(1, "<NAMESPACE>")]
        [Description("A base namespace to use for generated files. Only used when a defined namespace is not present.")]
        public string Namespace { get; set; } = string.Empty;

        [CommandOption("-o|--overwrite")]
        [Description("Overwrite any existing generated code.")]
        [DefaultValue(false)]
        public bool Overwrite { get; set; }

        [CommandOption("-d|--output-dir")]
        [Description("Directory to save generated C# files.")]
        public DirectoryInfo? OutputDirectory { get; set; }
    }

    private readonly IAnsiConsole _console;
    private readonly ICodeGeneratorResolver _codeGeneratorResolver;
    private readonly IIdlToAvroTranslator _idlTranslator;

    public CodeGenCommand(
        IAnsiConsole console,
        ICodeGeneratorResolver codeGeneratorResolver,
        IIdlToAvroTranslator idlTranslator
    )
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(codeGeneratorResolver);
        ArgumentNullException.ThrowIfNull(idlTranslator);

        _console = console;
        _codeGeneratorResolver = codeGeneratorResolver;
        _idlTranslator = idlTranslator;
    }

    public override ValidationResult Validate(CommandContext context, Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.InputFile))
            return ValidationResult.Error("An input file must be provided.");

        if (!File.Exists(settings.InputFile))
            return ValidationResult.Error($"An input file could not be found at: {settings.InputFile}");

        if (!string.IsNullOrWhiteSpace(settings.Namespace) && !CsharpValidation.IsValidCsharpNamespace(settings.Namespace))
            return ValidationResult.Error($"The value '{settings.Namespace}' is not a valid C# namespace.");

        return ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        AvroProtocol? protocol = null;
        IEnumerable<AvroSchema> schemas;
        var parseResult = await ParseIdl(settings.InputFile, cancellationToken);

        if (TryParseAvroProtocol(settings.InputFile, out var inputProtocol))
        {
            protocol = inputProtocol;
            schemas = [.. inputProtocol.Types];
        }
        else if (TryParseAvroSchema(settings.InputFile, out var inputSchema))
        {
            schemas = [inputSchema];
        }
        else if (parseResult.Success)
        {
            protocol = parseResult.Result.Match(p => p, _ => (AvroProtocol?)null);
            schemas = parseResult.Result.Match(p => p.Types, s => [s]);
        }
        else
        {
            _console.MarkupLine("[red]Input file unable to be parsed as one of Avro IDL, JSON protocol or JSON schema.[/]");
            return ErrorCode.Error;
        }

        var outputDir = settings.OutputDirectory ?? new DirectoryInfo(Directory.GetCurrentDirectory());

        try
        {
            if (protocol != null)
            {
                var outputFilePath = Path.Combine(outputDir.FullName, protocol.Name + ".cs");

                if (File.Exists(outputFilePath) && !settings.Overwrite)
                {
                    _console.MarkupLine("[red]Unable to generate C# files. A file already exists.[/]");
                    _console.MarkupLineInterpolated($"[red]    {outputFilePath}[/]");
                    return ErrorCode.Error;
                }
            }

            var filenames = schemas
                .Select(s => Path.Combine(outputDir.FullName, s.Name + ".cs"))
                .ToList();

            var existingFiles = filenames.Where(File.Exists).ToList();
            if (existingFiles.Count > 0 && !settings.Overwrite)
            {
                _console.MarkupLine("[red]Unable to generate C# files. One or more files exist.[/]");
                foreach (var existingFile in existingFiles)
                    _console.MarkupLineInterpolated($"[red]    {existingFile}[/]");
                return ErrorCode.Error;
            }

            if (protocol != null)
            {
                if (protocol.Messages.Count == 0)
                {
                    _console.MarkupLineInterpolated($"[yellow]Skipping protocol message generation. Protocol '{protocol.Name}' has no messages[/]");
                }
                else
                {
                    var outputFilePath = Path.Combine(outputDir.FullName, protocol.Name + ".cs");
                    var protocolGenerator = _codeGeneratorResolver.Resolve<AvroProtocol>()!;
                    var protocolOutput = protocolGenerator.Generate(protocol, settings.Namespace);

                    if (File.Exists(outputFilePath))
                        File.Delete(outputFilePath);

                    await File.WriteAllTextAsync(outputFilePath, protocolOutput, cancellationToken);
                    _console.MarkupLineInterpolated($"[green]Generated {outputFilePath}[/]");
                }
            }

            foreach (var schema in schemas)
            {
                var outputFilePath = Path.Combine(outputDir.FullName, schema.Name + ".cs");

                var schemaOutput = schema.Tag switch
                {
                    AvroSchema.Type.Enumeration => _codeGeneratorResolver.Resolve<EnumSchema>()!.Generate((EnumSchema)schema, settings.Namespace),
                    AvroSchema.Type.Fixed => _codeGeneratorResolver.Resolve<FixedSchema>()!.Generate((FixedSchema)schema, settings.Namespace),
                    AvroSchema.Type.Error => _codeGeneratorResolver.Resolve<RecordSchema>()!.Generate((RecordSchema)schema, settings.Namespace),
                    AvroSchema.Type.Record => _codeGeneratorResolver.Resolve<RecordSchema>()!.Generate((RecordSchema)schema, settings.Namespace),
                    _ => null
                };

                if (string.IsNullOrWhiteSpace(schemaOutput))
                    continue;

                if (File.Exists(outputFilePath))
                    File.Delete(outputFilePath);

                await File.WriteAllTextAsync(outputFilePath, schemaOutput, cancellationToken);
                _console.MarkupLineInterpolated($"[green]Generated {outputFilePath}[/]");
            }

            return ErrorCode.Success;
        }
        catch (Exception ex)
        {
            _console.MarkupLine("[red]Failed to generate C# files.[/]");
            _console.MarkupLineInterpolated($"[red]    {ex.Message}[/]");

            return ErrorCode.Error;
        }
    }

    private static bool TryParseAvroProtocol(string filePath, out AvroProtocol protocol)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            protocol = AvroProtocol.Parse(json);
            return true;
        }
        catch
        {
            protocol = default!;
            return false;
        }
    }

    private static bool TryParseAvroSchema(string filePath, out AvroSchema schema)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            schema = AvroSchema.Parse(json);
            return true;
        }
        catch
        {
            schema = default!;
            return false;
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