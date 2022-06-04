using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SJP.Avro.AvroTool.Commands;

namespace SJP.Avro.AvroTool;

internal static class Program
{
    private const string TitleText = @"    ___                 ______            __
   /   |_   ___________/_  __/___  ____  / /
  / /| | | / / ___/ __ \/ / / __ \/ __ \/ /
 / ___ | |/ / /  / /_/ / / / /_/ / /_/ / /
/_/  |_|___/_/   \____/_/  \____/\____/_/

The helpful Avro compiler tool.

";

    public static Task<int> Main(string[] args)
    {
        var root = new RootCommand();

        root.AddCommand(new IdlCommand());
        root.AddCommand(new IdlToSchemataCommand());
        root.AddCommand(new CodeGenCommand());

        var builder = new CommandLineBuilder(root);
        builder.UseHelp(ctx =>
        {
            ctx.HelpBuilder.CustomizeLayout(_ =>
                HelpBuilder.Default.GetLayout().Skip(1).Prepend(hctx => hctx.Output.Write(TitleText)));
        });
        builder.UseVersionOption();
        builder.UseParseErrorReporting();
        builder.CancelOnProcessTermination();
        builder.UseExceptionHandler(HandleException);

        var parser = builder.Build();
        return parser.InvokeAsync(args);
    }

    private static void HandleException(Exception exception, InvocationContext context)
    {
        context.Console.ResetTerminalForegroundColor();
        context.Console.SetTerminalForegroundRed();

        if (exception is TargetInvocationException tie &&
            tie.InnerException is not null)
        {
            exception = tie.InnerException;
        }

        // take the first if present
        if (exception is AggregateException ae && ae.InnerExceptions.Count > 0)
        {
            exception = ae.InnerExceptions[0];
        }

        if (exception is OperationCanceledException)
        {
            context.Console.Error.WriteLine("...canceled.");
        }
        else if (exception is CommandException command)
        {
            context.Console.Error.WriteLine($"The '{context.ParseResult.CommandResult.Command.Name}' command failed:");
            context.Console.Error.WriteLine($"    {command.Message}");

            if (command.InnerException != null)
            {
                context.Console.Error.WriteLine();
                context.Console.Error.WriteLine(command.InnerException.ToString());
            }
        }
        else
        {
            context.Console.Error.WriteLine("An unhandled exception has occurred: ");
            context.Console.Error.WriteLine(exception.ToString());
        }

        context.Console.ResetTerminalForegroundColor();

        context.ExitCode = 1;
    }
}