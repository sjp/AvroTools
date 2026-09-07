using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SJP.Avro.Tools.CodeGen;
using SJP.Avro.Tools.Idl;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Extensions.DependencyInjection;

namespace AvroTool;

internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        // Route status and diagnostic output to standard error so that standard
        // output carries only command payloads (e.g. 'idl --stdout'), keeping the
        // tool clean to use in shell pipelines.
        var errorConsole = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Detect,
            ColorSystem = ColorSystemSupport.Detect,
            Out = new AnsiConsoleOutput(Console.Error),
        });

        var services = new ServiceCollection();
        services.AddSingleton<IAnsiConsole>(errorConsole);
        services.AddSingleton<IStandardStreams, ConsoleStandardStreams>();
        services.AddTransient<ICodeGeneratorResolver, CodeGeneratorResolver>();
        services.AddTransient<IIdlFileReader, PhysicalIdlFileReader>();
        services.AddTransient<IIdlToAvroTranslator, IdlToAvroTranslator>();
        using var registrar = new DependencyInjectionRegistrar(services);

        var app = new CommandApp(registrar);

        app.Configure(config =>
        {
            config.SetApplicationName("avrotool");
            config.SetApplicationVersion(GetVersion());

            // Spectre special-cases the console it injects into commands and uses for
            // its own diagnostics, so configure it explicitly (DI registration alone
            // is not honoured) to keep standard output a clean payload channel.
            config.ConfigureConsole(errorConsole);

            foreach (var command in CommandCatalogue.Commands)
                command.Register(config);

            config.PropagateExceptions();
            config.ValidateExamples();
            config.SetExceptionHandler((ex, _) =>
            {
                errorConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
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