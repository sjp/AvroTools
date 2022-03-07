using SJP.Avro.AvroTool.Handlers;
using SJP.Avro.Tools;
using SJP.Avro.Tools.CodeGen;
using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.IO;
using System.Threading;

namespace SJP.Avro.AvroTool.Commands
{
    internal sealed class CodeGenCommand : Command
    {
        public CodeGenCommand()
            : base("codegen", "Generates C# code for a given Avro IDL, protocol or schema.")
        {
            var inputFileArg = new Argument<FileInfo>("input", "An IDL, protocol or schema file to generate C# code from.");
            AddArgument(inputFileArg);

            var namespaceArg = new Argument<string>("namespace", "A base namespace to use for generated files.");
            AddArgument(namespaceArg);

            var overwriteOption = new Option<bool>(
                "--overwrite",
                getDefaultValue: static () => false,
                description: "Overwrite any existing generated code."
            );
            AddOption(overwriteOption);

            var outputDirectoryOption = new Option<DirectoryInfo?>(
                "--output-dir",
                getDefaultValue: static () => null,
                description: "Directory to save ."
            );
            AddOption(outputDirectoryOption);

            Handler = CommandHandler.Create<IConsole, FileInfo, bool, string, DirectoryInfo?, CancellationToken>(static (console, input, overwrite, @namespace, outputDir, cancellationToken) =>
            {
                var handler = new CodeGenCommandHandler(
                    console,
                    new Tools.Idl.IdlTokenizer(),
                    new IdlCompiler(new DefaultFileProvider()),
                    new CodeGeneratorResolver()
                );

                return handler.HandleCommandAsync(input, overwrite, @namespace, outputDir, cancellationToken);
            });
        }
    }
}
