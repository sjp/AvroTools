using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.IO;
using System.Threading;
using AvroTool.Handlers;
using SJP.Avro.Tools;
using SJP.Avro.Tools.Idl;

namespace AvroTool.Commands;

internal sealed class IdlToSchemataCommand : Command
{
    public IdlToSchemataCommand()
        : base("idl2schemata", "Extract JSON schemata of the types from an Avro IDL file.")
    {
        var inputFileArg = new Argument<FileInfo>("idlfile", "An IDL file to compile.");
        AddArgument(inputFileArg);

        var overwriteOption = new Option<bool>(
            "--overwrite",
            getDefaultValue: static () => false,
            description: "Overwrite any existing types."
        );
        AddOption(overwriteOption);

        var outputDirectoryOption = new Option<DirectoryInfo?>(
            "--output-dir",
            getDefaultValue: static () => null,
            description: "Directory to save schemata."
        );
        AddOption(outputDirectoryOption);

        Handler = CommandHandler.Create<IConsole, FileInfo, bool, DirectoryInfo?, CancellationToken>(static (console, idlfile, overwrite, outputDir, cancellationToken) =>
        {
            var handler = new IdlToSchemataCommandHandler(
                console,
                new IdlTokenizer(),
                new IdlCompiler(new DefaultFileProvider())
            );

            return handler.HandleCommandAsync(idlfile, overwrite, outputDir, cancellationToken);
        });
    }
}