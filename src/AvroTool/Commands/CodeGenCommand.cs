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
        [Description("A base namespace to use for generated files. Only used, and only required, when a type to be generated does not declare a namespace of its own.")]
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
        [Description("Overwrite existing output files.")]
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

    private readonly IStatusConsole _console;
    private readonly IStandardStreams _streams;
    private readonly ICodeGeneratorResolver _codeGeneratorResolver;
    private readonly IIdlToAvroTranslator _idlTranslator;

    public CodeGenCommand(
        IStatusConsole console,
        IStandardStreams streams,
        ICodeGeneratorResolver codeGeneratorResolver,
        IIdlToAvroTranslator idlTranslator
    )
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(streams);
        ArgumentNullException.ThrowIfNull(codeGeneratorResolver);
        ArgumentNullException.ThrowIfNull(idlTranslator);

        _console = console;
        _streams = streams;
        _codeGeneratorResolver = codeGeneratorResolver;
        _idlTranslator = idlTranslator;
    }

    protected override ValidationResult Validate(CommandContext context, Settings settings)
    {
        var inputResult = settings.FromStandardInput
            ? InputValidation.ValidateStandardInputAlone(settings.InputFiles, "Input files")
            : InputValidation.Validate(settings.InputFiles, "input");
        if (!inputResult.Successful)
            return inputResult;

        if (!string.IsNullOrWhiteSpace(settings.BaseNamespace) && !CsharpValidation.IsValidCsharpNamespace(settings.BaseNamespace))
            return ValidationResult.Error($"The value '{settings.BaseNamespace}' is not a valid C# namespace.");

        return ValidationResult.Success();
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

        var collector = new OutputCollector(settings.Overwrite);

        if (settings.FromStandardInput)
        {
            var content = await InputReader.TryReadAllTextAsync(_streams, true, null, InputSource.StandardInputName, _console, cancellationToken);
            if (content == null)
                return ErrorCode.Error;

            var baseDirectory = InputSource.ImportBaseDirectory(true, null);
            var ok = await ProcessAsync(content, InputSource.StandardInputName, baseDirectory, settings, outputDir, collector, cancellationToken);
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
            var content = await InputReader.TryReadAllTextAsync(_streams, false, file, file, _console, cancellationToken);

            var ok = false;
            if (content != null)
            {
                var baseDirectory = InputSource.ImportBaseDirectory(false, file);
                ok = await ProcessAsync(content, file, baseDirectory, settings, outputDir, collector, cancellationToken);
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
        string inputContent,
        string source,
        string baseDirectory,
        Settings settings,
        DirectoryInfo outputDir,
        OutputCollector collector,
        CancellationToken cancellationToken)
    {
        var resolution = await AvroInputResolver.ResolveAsync(inputContent, _idlTranslator, baseDirectory, cancellationToken);
        if (resolution.Input == null)
        {
            _console.MarkupLineInterpolated($"[red]{resolution.FailureMessage(source)}[/]");
            return false;
        }

        var input = resolution.Input;

        var codeGenOptions = new CodeGenOptions(RequiredProperties: settings.Required, InitOnlyProperties: settings.InitOnly);

        var protocol = input.Protocol;
        IEnumerable<AvroSchema> schemas = input.Schemas;

        try
        {
            // A protocol's declared types may each reach the same nested type, so the
            // same named type can be seen more than once.
            var namedTypes = schemas
                .SelectMany(s => s.GetNamedTypes())
                .DistinctBy(static t => t.Fullname, StringComparer.Ordinal)
                .ToList();

            var generatesProtocol = protocol != null && protocol.Messages.Count > 0;
            if (protocol != null && !generatesProtocol)
                _console.MarkupLineInterpolated($"[yellow]Skipping protocol message generation. Protocol '{protocol.Name}' has no messages[/]");

            if (string.IsNullOrWhiteSpace(settings.BaseNamespace))
            {
                var missingNamespaces = namedTypes
                    .Where(static t => string.IsNullOrWhiteSpace(t.Namespace))
                    .Select(static t => t.Name)
                    .ToList();
                if (generatesProtocol && string.IsNullOrWhiteSpace(protocol!.Namespace))
                    missingNamespaces.Insert(0, protocol.Name);

                if (missingNamespaces.Count > 0)
                {
                    var names = string.Join(", ", missingNamespaces.Distinct(StringComparer.Ordinal));
                    _console.MarkupLineInterpolated($"[red]Unable to generate C# files from '{source}': no namespace is declared by {names}. Provide a base namespace with --namespace.[/]");
                    return false;
                }
            }

            // Generate every output this input produces before writing any of it, so collisions
            // within the input and with other inputs (and pre-existing files without --overwrite)
            // are settled while the output directory is still untouched.
            var reservations = new List<OutputReservation>();
            if (generatesProtocol)
            {
                var protocolGenerator = _codeGeneratorResolver.Resolve<AvroProtocol>()!;
                var protocolOutput = protocolGenerator.Generate(protocol!, settings.BaseNamespace, codeGenOptions);

                reservations.Add(new OutputReservation(
                    Path.Combine(outputDir.FullName, protocol!.Name + ".cs"),
                    $"protocol '{protocol.Name}'",
                    protocolOutput));
            }

            foreach (var namedType in namedTypes)
            {
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

                reservations.Add(new OutputReservation(
                    Path.Combine(outputDir.FullName, namedType.Fullname + ".cs"),
                    $"type '{namedType.Fullname}'",
                    schemaOutput));
            }

            var plan = collector.Reserve(reservations, source);
            if (plan.Error != null)
            {
                _console.MarkupLineInterpolated($"[red]Unable to generate C# files from '{source}': {plan.Error}[/]");
                return false;
            }

            await OutputCollector.WritePlanAsync(plan, _console, cancellationToken);

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
