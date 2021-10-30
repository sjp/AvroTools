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

namespace SJP.Avro.AvroTool.Handlers
{
    internal sealed class IdlCommandHandler
    {
        private readonly IdlTokenizer _tokenizer = new();
        private readonly IdlCompiler _compiler = new(new DefaultFileProvider());

        public async Task<int> HandleCommandAsync(IConsole console, FileInfo idlFile, bool overwrite, DirectoryInfo? outputDir, CancellationToken cancellationToken)
        {
            if (!idlFile.Exists)
            {
                WriteError(console, "An IDL file could not be found at: " + idlFile.FullName);
                return ErrorCode.Error;
            }

            if (!TryGetIdlTokens(console, idlFile, out var tokens))
                return ErrorCode.Error;

            if (!TryGetProtocol(console, tokens, out var protocol))
                return ErrorCode.Error;

            outputDir ??= idlFile.Directory!;

            var protocolFileName = protocol.Name.Value + ".avpr";
            var outputPath = Path.Combine(outputDir.FullName, protocolFileName);

            if (File.Exists(outputPath) && !overwrite)
            {
                WriteError(console, $"The output file path '{outputPath}' cannot be used");
                WriteError(console, "A file already exists. Consider using the 'overwrite' option.");
                return ErrorCode.Error;
            }

            try
            {
                var output = _compiler.Compile(idlFile.FullName, protocol);
                if (File.Exists(outputPath))
                    File.Delete(outputPath);

                await File.WriteAllTextAsync(outputPath, output, cancellationToken).ConfigureAwait(false);

                console.SetTerminalForegroundGreen();
                console.Out.Write($"Generated { outputPath }");
                console.ResetTerminalForegroundColor();

                return ErrorCode.Success;
            }
            catch (Exception ex)
            {
                console.SetTerminalForegroundRed();
                console.Error.WriteLine("Failed to generate Avro protocol file.");
                console.Error.Write("    " + ex.Message);
                console.ResetTerminalForegroundColor();

                return ErrorCode.Error;
            }
        }

        private bool TryGetIdlTokens(IConsole console, FileInfo idlFile, out TokenList<IdlToken> tokens)
        {
            var idlText = File.ReadAllText(idlFile.FullName);
            var tokenizeResult = _tokenizer.TryTokenize(idlText);

            if (!tokenizeResult.HasValue)
            {
                tokens = default;
                WriteError(console, "Unable to parse IDL document: " + tokenizeResult);
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

        private static bool TryGetProtocol(IConsole console, TokenList<IdlToken> tokens, out Protocol protocol)
        {
            var result = IdlTokenParsers.Protocol(tokens);

            if (!result.HasValue)
            {
                protocol = default!;
                WriteError(console, "Unable to parse protocol from IDL document: " + result.ErrorMessage);
            }
            else
            {
                protocol = result.Value;
            }

            return result.HasValue;
        }

        private static void WriteError(IConsole console, string errorMessage)
        {
            console.SetTerminalForegroundRed();
            console.Error.WriteLine(errorMessage);
            console.ResetTerminalForegroundColor();
        }
    }
}
