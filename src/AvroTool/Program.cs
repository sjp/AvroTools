using System.Reflection;
using System.Threading.Tasks;
using AvroTool.Commands;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvroTool;

internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        var app = new CommandApp();

        app.Configure(config =>
        {
            config.SetApplicationName("avrotool");
            config.SetApplicationVersion(GetVersion());

            config.AddCommand<IdlCommand>("idl").WithDescription("Generates a JSON protocol file from an Avro IDL file.");
            config.AddCommand<IdlToSchemataCommand>("idl2schemata").WithDescription("Extract JSON schemata of the types from an Avro IDL file.");
            config.AddCommand<CodeGenCommand>("codegen").WithDescription("Generates C# code for a given Avro IDL, protocol or schema.");

            config.PropagateExceptions();
            config.ValidateExamples();
            config.SetInterceptor(new HelpInterceptor());
            config.SetExceptionHandler((ex, _) =>
            {
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
            });
        });

        return app.RunAsync(args);
    }

    private static string GetVersion()
    {
        var assembly = Assembly.GetEntryAssembly()!;
        var assemblyVersion = assembly.GetName().Version!;
        return $"v{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
    }
}

internal sealed class HelpInterceptor : ICommandInterceptor
{
    public void Intercept(CommandContext context, CommandSettings settings)
    {
        AnsiConsole.Write(new FigletText("AvroTool").LeftJustified().Color(Color.Blue));
        AnsiConsole.WriteLine("The helpful Avro compiler tool.");
        AnsiConsole.WriteLine();
    }

    public void InterceptResult(CommandContext context, CommandSettings settings, ref int result)
    {
    }
}
