﻿using System;
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
using AvroProtocol = Avro.Protocol;
using AvroSchema = Avro.Schema;

namespace SJP.Avro.AvroTool.Handlers;

internal sealed class CodeGenCommandHandler
{
    private readonly IConsole _console;
    private readonly IdlTokenizer _tokenizer;
    private readonly IdlCompiler _compiler;
    private readonly ICodeGeneratorResolver _codeGeneratorResolver;

    public CodeGenCommandHandler(
        IConsole console,
        IdlTokenizer idlTokenizer,
        IdlCompiler idlCompiler,
        ICodeGeneratorResolver codeGeneratorResolver
    )
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _tokenizer = idlTokenizer ?? throw new ArgumentNullException(nameof(idlTokenizer));
        _compiler = idlCompiler ?? throw new ArgumentNullException(nameof(idlCompiler));
        _codeGeneratorResolver = codeGeneratorResolver ?? throw new ArgumentNullException(nameof(codeGeneratorResolver));
    }

    public async Task<int> HandleCommandAsync(FileInfo input, bool overwrite, string baseNamespace, DirectoryInfo? outputDir, CancellationToken cancellationToken)
    {
        if (!input.Exists)
        {
            WriteError("An input file could not be found at: " + input.FullName);
            return ErrorCode.Error;
        }

        AvroProtocol? protocol = null;
        var schemas = new List<AvroSchema>();
        if (TryParseAvroProtocol(input, out var inputProtocol))
        {
            protocol = inputProtocol;
            schemas = [..inputProtocol.Types];
        }
        else if (TryParseAvroSchema(input, out var inputSchema))
        {
            schemas = [inputSchema];
        }
        else if (TryParseAvroProtocolFromIdl(input, out var idlParsedProtocol))
        {
            protocol = idlParsedProtocol;
            schemas = [..idlParsedProtocol.Types];
        }
        else
        {
            WriteError("Input file unable to be parsed as one of Avro IDL, JSON protocol or JSON schema.");
            return ErrorCode.Error;
        }

        outputDir ??= new DirectoryInfo(Directory.GetCurrentDirectory());

        try
        {
            if (protocol != null)
            {
                var outputFilePath = Path.Combine(outputDir.FullName, protocol.Name + ".cs");

                if (File.Exists(outputFilePath) && !overwrite)
                {
                    WriteError("Unable to generate C# files. A file already exists.");
                    WriteError("    " + outputFilePath);
                    return ErrorCode.Error;
                }
            }

            var filenames = schemas.ConvertAll(s => Path.Combine(outputDir.FullName, s.Name + ".cs"));

            var existingFiles = filenames.Where(File.Exists).ToList();
            if (existingFiles.Count > 0 && !overwrite)
            {
                WriteError("Unable to generate C# files. One or more files exist.");
                foreach (var existingFile in existingFiles)
                    WriteError("    " + existingFile);
                return ErrorCode.Error;
            }

            if (protocol != null)
            {
                if (protocol.Messages.Count == 0)
                {
                    WriteWarning($"Skipping protocol message generation. Protocol '{protocol.Name}' has no messages");
                }
                else
                {
                    var outputFilePath = Path.Combine(outputDir.FullName, protocol.Name + ".cs");
                    var protocolGenerator = _codeGeneratorResolver.Resolve<AvroProtocol>()!;
                    var protocolOutput = protocolGenerator.Generate(protocol, baseNamespace);

                    if (File.Exists(outputFilePath))
                        File.Delete(outputFilePath);

                    await File.WriteAllTextAsync(outputFilePath, protocolOutput, cancellationToken).ConfigureAwait(false);
                    WriteSuccess("Generated " + outputFilePath);
                }
            }

            foreach (var schema in schemas)
            {
                var outputFilePath = Path.Combine(outputDir.FullName, schema.Name + ".cs");

                var schemaOutput = schema.Tag switch
                {
                    AvroSchema.Type.Enumeration => _codeGeneratorResolver.Resolve<EnumSchema>()!.Generate((EnumSchema)schema, baseNamespace),
                    AvroSchema.Type.Fixed => _codeGeneratorResolver.Resolve<FixedSchema>()!.Generate((FixedSchema)schema, baseNamespace),
                    AvroSchema.Type.Error => _codeGeneratorResolver.Resolve<RecordSchema>()!.Generate((RecordSchema)schema, baseNamespace),
                    AvroSchema.Type.Record => _codeGeneratorResolver.Resolve<RecordSchema>()!.Generate((RecordSchema)schema, baseNamespace),
                    _ => null
                };

                if (schemaOutput.IsNullOrWhiteSpace())
                    continue;

                if (File.Exists(outputFilePath))
                    File.Delete(outputFilePath);

                await File.WriteAllTextAsync(outputFilePath, schemaOutput, cancellationToken).ConfigureAwait(false);
                WriteSuccess("Generated " + outputFilePath);
            }

            return ErrorCode.Success;
        }
        catch (Exception ex)
        {
            _console.SetTerminalForegroundRed();
            _console.Error.WriteLine("Failed to generate C# files.");
            _console.Error.Write("    " + ex.Message);
            _console.ResetTerminalForegroundColor();

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

    private void WriteSuccess(string message)
    {
        _console.SetTerminalForegroundGreen();
        _console.Out.WriteLine(message);
        _console.ResetTerminalForegroundColor();
    }

    private void WriteWarning(string message)
    {
        _console.SetTerminalForegroundYellow();
        _console.Out.WriteLine(message);
        _console.ResetTerminalForegroundColor();
    }

    private void WriteError(string message)
    {
        _console.SetTerminalForegroundRed();
        _console.Error.WriteLine(message);
        _console.ResetTerminalForegroundColor();
    }
}