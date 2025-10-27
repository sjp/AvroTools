using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SJP.Avro.Tools;
using SJP.Avro.Tools.Idl;
using SJP.Avro.Tools.Idl.Model;
using Spectre.Console;
using Spectre.Console.Cli;
using Superpower.Model;
using AvroProtocol = Avro.Protocol;

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
    private readonly IIdlTokenizer _tokenizer;
    private readonly IIdlCompiler _compiler;

    public IdlToSchemataCommand(
        IAnsiConsole console,
        IIdlTokenizer tokenizer,
        IIdlCompiler compiler)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
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
        if (!TryGetIdlTokens(settings.IdlFile, out var tokens))
            return ErrorCode.Error;

        if (!TryGetProtocol(tokens, out var protocol))
            return ErrorCode.Error;

        var outputDir = settings.OutputDirectory ?? new DirectoryInfo(Directory.GetCurrentDirectory());

        try
        {
            var output = _compiler.Compile(settings.IdlFile, protocol);
            var avroProtocol = AvroProtocol.Parse(output);

            var filenames = avroProtocol.Types
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

            foreach (var schema in avroProtocol.Types)
            {
                var schemaFilename = Path.Combine(outputDir.FullName, schema.Name + ".avsc");
                if (File.Exists(schemaFilename))
                    File.Delete(schemaFilename);

                await File.WriteAllTextAsync(schemaFilename, schema.ToString(), cancellationToken);
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

    private bool TryGetIdlTokens(string idlFile, out TokenList<IdlToken> tokens)
    {
        var idlText = File.ReadAllText(idlFile);
        var tokenizeResult = _tokenizer.TryTokenize(idlText);

        if (!tokenizeResult.HasValue)
        {
            tokens = default;
            _console.MarkupLine($"[red]Unable to parse IDL document: {tokenizeResult}[/]");
        }
        else
        {
            var commentFreeTokens = tokenizeResult.Value
                .Where(t => t.Kind != IdlToken.Comment)
                .ToArray();

            tokens = new TokenList<IdlToken>(commentFreeTokens);
        }

        return tokenizeResult.HasValue;
    }

    private bool TryGetProtocol(TokenList<IdlToken> tokens, out Protocol protocol)
    {
        var result = IdlTokenParsers.Protocol(tokens);

        if (!result.HasValue)
        {
            protocol = default!;
            _console.MarkupLineInterpolated($"[red]Unable to parse protocol from IDL document: {result.ErrorMessage}[/]");
        }
        else
        {
            protocol = result.Value;
        }

        return result.HasValue;
    }
}