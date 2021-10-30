using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avro;
using SJP.Avro.Tools;
using SJP.Avro.Tools.CodeGen;
using SJP.Avro.Tools.Idl;
using Superpower.Model;
using AvroProtocol = global::Avro.Protocol;
using AvroSchema = global::Avro.Schema;

namespace SJP.Avro.AvroTool.Handlers
{
    internal sealed class CodeGenCommandHandler
    {
        private readonly IdlTokenizer _tokenizer = new();
        private readonly IdlCompiler _compiler = new(new DefaultFileProvider());

        private readonly AvroEnumGenerator _enumGenerator = new();
        private readonly AvroFixedGenerator _fixedGenerator = new();
        private readonly AvroProtocolGenerator _protocolGenerator = new();
        private readonly AvroRecordGenerator _recordGenerator = new();

        public async Task<int> HandleCommandAsync(IConsole console, FileInfo input, bool overwrite, string baseNamespace, DirectoryInfo? outputDir, CancellationToken cancellationToken)
        {
            if (!input.Exists)
            {
                WriteError(console, "An input file could not be found at: " + input.FullName);
                return ErrorCode.Error;
            }

            AvroProtocol? protocol = null;
            var schemas = new List<AvroSchema>();
            if (TryParseAvroProtocol(input, out var inputProtocol))
            {
                protocol = inputProtocol;
                schemas = inputProtocol.Types.ToList();
            }
            else if (TryParseAvroSchema(input, out var inputSchema))
            {
                schemas = new List<AvroSchema> { inputSchema };
            }
            else if (TryParseAvroProtocolFromIdl(input, out var idlParsedProtocol))
            {
                protocol = idlParsedProtocol;
                schemas = idlParsedProtocol.Types.ToList();
            }

            outputDir ??= input.Directory!;

            try
            {
                if (protocol != null)
                {
                    var outputFilePath = Path.Combine(outputDir.FullName, protocol.Name + ".cs");

                    if (File.Exists(outputFilePath) && !overwrite)
                    {
                        WriteError(console, "Unable to generate C# files. A file already exists.");
                        WriteError(console, "    " + outputFilePath);
                        return ErrorCode.Error;
                    }
                }

                var filenames = schemas.ConvertAll(s => Path.Combine(outputDir.FullName, s.Name + ".cs"));

                var existingFiles = filenames.Where(File.Exists).ToList();
                if (existingFiles.Count > 0 && !overwrite)
                {
                    WriteError(console, "Unable to generate C# files. One or more files exist.");
                    foreach (var existingFile in existingFiles)
                        WriteError(console, "    " + existingFile);
                    return ErrorCode.Error;
                }

                if (protocol != null)
                {
                    if (protocol.Messages.Count == 0)
                    {
                        WriteWarning(console, $"Skipping protocol message generation. Protocol '{ protocol.Name }' has no messages");
                    }
                    else
                    {
                        var outputFilePath = Path.Combine(outputDir.FullName, protocol.Name + ".cs");
                        var protocolOutput = _protocolGenerator.Generate(protocol, baseNamespace);

                        if (File.Exists(outputFilePath))
                            File.Delete(outputFilePath);

                        await File.WriteAllTextAsync(outputFilePath, protocolOutput, cancellationToken).ConfigureAwait(false);
                        WriteSuccess(console, "Generated " + outputFilePath);
                    }
                }

                foreach (var schema in schemas)
                {
                    var outputFilePath = Path.Combine(outputDir.FullName, schema.Name + ".cs");
                    var schemaOutput = schema.Tag switch
                    {
                        AvroSchema.Type.Enumeration => _enumGenerator.Generate(schema as EnumSchema, baseNamespace),
                        AvroSchema.Type.Fixed => _fixedGenerator.Generate(schema as FixedSchema, baseNamespace),
                        AvroSchema.Type.Error => _recordGenerator.Generate(schema as RecordSchema, baseNamespace),
                        AvroSchema.Type.Record => _recordGenerator.Generate(schema as RecordSchema, baseNamespace),
                        _ => null
                    };
                    if (schemaOutput.IsNullOrWhiteSpace())
                        continue;

                    if (File.Exists(outputFilePath))
                        File.Delete(outputFilePath);

                    await File.WriteAllTextAsync(outputFilePath, schemaOutput, cancellationToken).ConfigureAwait(false);
                    WriteSuccess(console, "Generated " + outputFilePath);
                }

                return ErrorCode.Success;
            }
            catch (Exception ex)
            {
                console.SetTerminalForegroundRed();
                console.Error.WriteLine("Failed to generate C# files.");
                console.Error.Write("    " + ex.Message);
                console.ResetTerminalForegroundColor();

                return ErrorCode.Error;
            }
        }

        private static bool TryParseAvroProtocol(FileInfo fileInfo, out AvroProtocol protocol)
        {
            try
            {
                var json = File.ReadAllText(fileInfo.FullName);
                protocol = AvroProtocol.Parse(json);
                return true;
            }
            catch
            {
                protocol = default!;
                return false;
            }
        }

        private static bool TryParseAvroSchema(FileInfo fileInfo, out AvroSchema schema)
        {
            try
            {
                var json = File.ReadAllText(fileInfo.FullName);
                schema = AvroSchema.Parse(json);
                return true;
            }
            catch
            {
                schema = default!;
                return false;
            }
        }

        private bool TryParseAvroProtocolFromIdl(FileInfo fileInfo, out AvroProtocol protocol)
        {
            if (!TryGetIdlTokens(fileInfo, out var tokens))
            {
                protocol = default!;
                return false;
            }

            if (!TryGetProtocol(tokens, out var parsedProtocol))
            {
                protocol = default!;
                return false;
            }

            try
            {
                var output = _compiler.Compile(fileInfo.FullName, parsedProtocol);
                protocol = AvroProtocol.Parse(output);
                return true;
            }
            catch
            {
                protocol = default!;
                return false;
            }
        }

        private bool TryGetIdlTokens(FileInfo idlFile, out TokenList<IdlToken> tokens)
        {
            var idlText = File.ReadAllText(idlFile.FullName);
            var tokenizeResult = _tokenizer.TryTokenize(idlText);

            if (!tokenizeResult.HasValue)
            {
                tokens = default;
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

        private static bool TryGetProtocol(TokenList<IdlToken> tokens, out Tools.Idl.Model.Protocol protocol)
        {
            var result = IdlTokenParsers.Protocol(tokens);

            if (!result.HasValue)
            {
                protocol = default!;
            }
            else
            {
                protocol = result.Value;
            }

            return result.HasValue;
        }

        private static void WriteSuccess(IConsole console, string message)
        {
            console.SetTerminalForegroundGreen();
            console.Out.WriteLine(message);
            console.ResetTerminalForegroundColor();
        }

        private static void WriteWarning(IConsole console, string message)
        {
            console.SetTerminalForegroundYellow();
            console.Out.WriteLine(message);
            console.ResetTerminalForegroundColor();
        }

        private static void WriteError(IConsole console, string message)
        {
            console.SetTerminalForegroundRed();
            console.Error.WriteLine(message);
            console.ResetTerminalForegroundColor();
        }
    }
}
