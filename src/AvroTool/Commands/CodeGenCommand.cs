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
using AvroProtocol = Avro.Protocol;
using AvroSchema = Avro.Schema;

namespace AvroTool.Commands;

internal sealed class CodeGenCommand : AsyncCommand<CodeGenCommand.Settings>
{
    private static readonly string[] InputExtensions = [".avdl", ".avpr", ".avsc"];

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[INPUT_FILES]")]
        [Description("One or more IDL, protocol or schema files, directories or glob patterns to generate C# code from. Omit and use --stdin to read from standard input.")]
        public string[] InputFiles { get; set; } = [];

        [CommandOption("-n|--namespace")]
        [Description("A base namespace to use for generated files. Only used when a defined namespace is not present.")]
        public string? Namespace { get; set; }

        [CommandOption("--stdin")]
        [Description("Read the input from standard input instead of a file.")]
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

        /// <summary>The effective base namespace for generated files.</summary>
        public string BaseNamespace => Namespace ?? string.Empty;

        [CommandOption("-o|--overwrite")]
        [Description("Overwrite any existing generated code.")]
        [DefaultValue(false)]
        public bool Overwrite { get; set; }

        [CommandOption("-d|--output-dir")]
        [Description("Directory to save generated C# files.")]
        public DirectoryInfo? OutputDirectory { get; set; }

        [CommandOption("--required")]
        [Description("Mark non-optional properties (no Avro-declared default, not a nullable union) with the 'required' modifier.")]
        [DefaultValue(false)]
        public bool Required { get; set; }

        [CommandOption("--init-only")]
        [Description("Generate 'init'-only properties instead of settable ones. A private backing field is used so deserialization still works.")]
        [DefaultValue(false)]
        public bool InitOnly { get; set; }
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

    protected override ValidationResult Validate(CommandContext context, Settings settings)
    {
        if (!settings.FromStandardInput)
        {
            var inputResult = InputValidation.Validate(settings.InputFiles, "input");
            if (!inputResult.Successful)
                return inputResult;
        }

        if (!string.IsNullOrWhiteSpace(settings.BaseNamespace) && !CsharpValidation.IsValidCsharpNamespace(settings.BaseNamespace))
            return ValidationResult.Error($"The value '{settings.BaseNamespace}' is not a valid C# namespace.");

        return ValidationResult.Success();
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var outputDir = settings.OutputDirectory ?? new DirectoryInfo(Directory.GetCurrentDirectory());
        var collector = new OutputCollector(settings.Overwrite);

        if (settings.FromStandardInput)
        {
            var content = await InputSource.ReadAllTextAsync(true, null, cancellationToken);
            var ok = await ProcessAsync(content, "<stdin>", settings, outputDir, collector, cancellationToken);
            return ok ? ErrorCode.Success : ErrorCode.Error;
        }

        var expansion = InputExpander.Expand(settings.InputFiles, InputExtensions, !settings.NoRecursive);
        foreach (var token in expansion.UnmatchedTokens)
            _console.MarkupLineInterpolated($"[red]No input files matched: {token}[/]");

        if (expansion.Files.Count == 0)
        {
            _console.MarkupLine("[red]No input files were found to generate code from.[/]");
            return ErrorCode.Error;
        }

        var anyFailed = expansion.UnmatchedTokens.Count > 0;
        foreach (var file in expansion.Files)
        {
            var content = await File.ReadAllTextAsync(file, cancellationToken);
            var ok = await ProcessAsync(content, file, settings, outputDir, collector, cancellationToken);
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
        string inputContent,
        string source,
        Settings settings,
        DirectoryInfo outputDir,
        OutputCollector collector,
        CancellationToken cancellationToken)
    {
        var input = await AvroInputResolver.ResolveAsync(inputContent, _idlTranslator, cancellationToken);
        if (input == null)
        {
            _console.MarkupLineInterpolated($"[red]Input '{source}' unable to be parsed as one of Avro IDL, JSON protocol or JSON schema.[/]");
            return false;
        }

        var codeGenOptions = new CodeGenOptions(RequiredProperties: settings.Required, InitOnlyProperties: settings.InitOnly);

        var protocol = input.Protocol;
        IEnumerable<AvroSchema> schemas = input.Schemas;

        try
        {
            var namedTypes = schemas
                .SelectMany(s => s.GetNamedTypes())
                .ToList();

            // Reserve every output this input could produce so collisions with other inputs
            // (and pre-existing files without --overwrite) are reported before anything is written.
            var reservations = new List<string>();
            if (protocol != null)
                reservations.Add(Path.Combine(outputDir.FullName, protocol.Name + ".cs"));
            reservations.AddRange(namedTypes.Select(s => Path.Combine(outputDir.FullName, s.Fullname + ".cs")));

            var reserveError = collector.Reserve(reservations, source);
            if (reserveError != null)
            {
                _console.MarkupLineInterpolated($"[red]Unable to generate C# files from '{source}': {reserveError}[/]");
                return false;
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
                    var protocolOutput = protocolGenerator.Generate(protocol, settings.BaseNamespace, codeGenOptions);

                    await OutputCollector.WriteAsync(outputFilePath, protocolOutput, cancellationToken);
                    _console.MarkupLineInterpolated($"[green]Generated {outputFilePath}[/]");
                }
            }

            foreach (var namedType in namedTypes)
            {
                var outputFilePath = Path.Combine(outputDir.FullName, namedType.Fullname + ".cs");

                var schemaOutput = namedType.Tag switch
                {
                    AvroSchema.Type.Enumeration => _codeGeneratorResolver.Resolve<EnumSchema>()!.Generate((EnumSchema)namedType, settings.BaseNamespace, codeGenOptions),
                    AvroSchema.Type.Fixed => _codeGeneratorResolver.Resolve<FixedSchema>()!.Generate((FixedSchema)namedType, settings.BaseNamespace, codeGenOptions),
                    AvroSchema.Type.Error => _codeGeneratorResolver.Resolve<RecordSchema>()!.Generate((RecordSchema)namedType, settings.BaseNamespace, codeGenOptions),
                    AvroSchema.Type.Record => _codeGeneratorResolver.Resolve<RecordSchema>()!.Generate((RecordSchema)namedType, settings.BaseNamespace, codeGenOptions),
                    _ => null
                };

                if (string.IsNullOrWhiteSpace(schemaOutput))
                    continue;

                await OutputCollector.WriteAsync(outputFilePath, schemaOutput, cancellationToken);
                _console.MarkupLineInterpolated($"[green]Generated {outputFilePath}[/]");
            }

            return true;
        }
        catch (Exception ex)
        {
            _console.MarkupLineInterpolated($"[red]Failed to generate C# files from '{source}'.[/]");
            _console.MarkupLineInterpolated($"[red]    {ex.Message}[/]");

            return false;
        }
    }
}
