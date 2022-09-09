using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SJP.Avro.Tools;
using SJP.Avro.Tools.Idl;
using SJP.Avro.Tools.Idl.Model;
using Superpower.Model;

namespace SJP.Avro.AvroTool.Handlers;

internal sealed class IdlCommandHandler
{
    private readonly IConsole _console;
    private readonly IdlTokenizer _tokenizer;
    private readonly IdlCompiler _compiler;

    public IdlCommandHandler(
        IConsole console,
        IdlTokenizer idlTokenizer,
        IdlCompiler idlCompiler
    )
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _tokenizer = idlTokenizer ?? throw new ArgumentNullException(nameof(idlTokenizer));
        _compiler = idlCompiler ?? throw new ArgumentNullException(nameof(idlCompiler));
    }

    public async Task<int> HandleCommandAsync(FileInfo idlFile, bool overwrite, DirectoryInfo? outputDir, CancellationToken cancellationToken)
    {
        if (!idlFile.Exists)
        {
            WriteError("An IDL file could not be found at: " + idlFile.FullName);
            return ErrorCode.Error;
        }

        if (!TryGetIdlTokens(idlFile, out var tokens))
            return ErrorCode.Error;

        if (!TryGetProtocol(tokens, out var protocol))
            return ErrorCode.Error;

        outputDir ??= new DirectoryInfo(Directory.GetCurrentDirectory());

        var protocolFileName = protocol.Name.Value + ".avpr";
        var outputPath = Path.Combine(outputDir.FullName, protocolFileName);

        if (File.Exists(outputPath) && !overwrite)
        {
            WriteError($"The output file path '{outputPath}' cannot be used");
            WriteError("A file already exists. Consider using the 'overwrite' option.");
            return ErrorCode.Error;
        }

        try
        {
            var output = _compiler.Compile(idlFile.FullName, protocol);
            if (File.Exists(outputPath))
                File.Delete(outputPath);

            await File.WriteAllTextAsync(outputPath, output, cancellationToken).ConfigureAwait(false);

            _console.SetTerminalForegroundGreen();
            _console.Out.Write($"Generated {outputPath}");
            _console.ResetTerminalForegroundColor();

            return ErrorCode.Success;
        }
        catch (Exception ex)
        {
            _console.SetTerminalForegroundRed();
            _console.Error.WriteLine("Failed to generate Avro protocol file.");
            _console.Error.Write("    " + ex.Message);
            _console.ResetTerminalForegroundColor();

            return ErrorCode.Error;
        }
    }

    private bool TryGetIdlTokens(FileInfo idlFile, out TokenList<IdlToken> tokens)
    {
        var idlText = File.ReadAllText(idlFile.FullName);
        var tokenizeResult = _tokenizer.TryTokenize(idlText);

        if (!tokenizeResult.HasValue)
        {
            tokens = default;
            WriteError("Unable to parse IDL document: " + tokenizeResult);
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
            WriteError("Unable to parse protocol from IDL document: " + result.ErrorMessage);
        }
        else
        {
            protocol = result.Value;
        }

        return result.HasValue;
    }

    private void WriteError(string errorMessage)
    {
        _console.SetTerminalForegroundRed();
        _console.Error.WriteLine(errorMessage);
        _console.ResetTerminalForegroundColor();
    }
}