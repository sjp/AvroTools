using System;
using System.IO;
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
    /// <summary>
    /// The width messages are laid out at when they are not going to a terminal. Wide enough
    /// that no message reaches it, so nothing is wrapped.
    /// </summary>
    private const int UnwrappedWidth = int.MaxValue;

    public static Task<int> Main(string[] args)
    {
        // Route status and diagnostic output to standard error so that standard
        // output carries only command payloads (e.g. 'idl --stdout'), keeping the
        // tool clean to use in shell pipelines.
        var errorConsole = CreateErrorConsole(Console.Error);

        var services = new ServiceCollection();
        RegisterServices(services, errorConsole, new ConsoleStandardStreams());
        using var registrar = new DependencyInjectionRegistrar(services);

        var app = new CommandApp(registrar);
        app.Configure(config => Configure(config, errorConsole));

        return app.RunAsync(args);
    }

    /// <summary>
    /// Creates the console that status and diagnostic messages are written to.
    /// </summary>
    /// <param name="writer">The writer the console renders to.</param>
    /// <returns>A console that renders to <paramref name="writer"/>.</returns>
    public static IAnsiConsole CreateErrorConsole(TextWriter writer)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Detect,
            ColorSystem = ColorSystemSupport.Detect,
            Out = new AnsiConsoleOutput(writer),
        });

        // Every line is laid out to the width of the terminal, and when the destination is
        // not one that width is assumed to be 80 columns. A pipe, a file or a CI log has no
        // width of its own, so wrapping there splits paths and messages mid-word and leaves
        // them impossible to copy, click or search for. Lay them out unwrapped instead.
        if (!console.Profile.Out.IsTerminal)
            console.Profile.Width = UnwrappedWidth;

        return console;
    }

    /// <summary>
    /// Registers the services the commands take as dependencies.
    /// </summary>
    /// <param name="services">The collection to register into.</param>
    /// <param name="errorConsole">The console status and diagnostic messages are written to.</param>
    /// <param name="streams">The streams command payloads are read from and written to.</param>
    public static void RegisterServices(IServiceCollection services, IAnsiConsole errorConsole, IStandardStreams streams)
    {
        services.AddSingleton(errorConsole);
        services.AddSingleton(streams);
        services.AddTransient<ICodeGeneratorResolver, CodeGeneratorResolver>();
        services.AddTransient<IIdlFileReader, PhysicalIdlFileReader>();
        services.AddTransient<IIdlToAvroTranslator, IdlToAvroTranslator>();
    }

    /// <summary>
    /// Applies the command-line application's configuration: the commands it exposes, the
    /// console its own output goes to, and how a failure is reported. Separate from
    /// <see cref="Main"/> so that the configuration the released tool runs with is the one
    /// under test.
    /// </summary>
    /// <param name="config">The configurator to apply the configuration to.</param>
    /// <param name="errorConsole">The console errors are written to.</param>
    public static void Configure(IConfigurator config, IAnsiConsole errorConsole)
    {
        config.SetApplicationName("avrotool");
        config.SetApplicationVersion(GetVersion());

        // Reject options that no command declares. Left lenient, a mistyped flag is
        // accepted in silence and swallows the argument that follows it, so a command
        // quietly does something other than what was asked for.
        config.UseStrictParsing();

        // Spectre special-cases the console it injects into commands and uses for
        // its own diagnostics, so configure it explicitly (DI registration alone
        // is not honoured) to keep standard output a clean payload channel.
        config.ConfigureConsole(errorConsole);

        foreach (var command in CommandCatalogue.Commands)
            command.Register(config);

        config.ValidateExamples();

        // A command line that cannot be parsed, an argument that cannot be converted and a
        // failed validation are all user errors, so they are reported as the message alone
        // and exit non-zero. A stack trace only helps with a fault in the tool itself, so it
        // is kept for exceptions that are not raised by the command-line framework.
        config.SetExceptionHandler((ex, _) =>
        {
            switch (ex)
            {
                case CommandAppException { Pretty: { } pretty }:
                    errorConsole.Write(pretty);
                    break;
                case CommandAppException:
                    errorConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
                    break;
                default:
                    errorConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
                    break;
            }

            return ErrorCode.Error;
        });
    }

    private static string GetVersion()
    {
        var assemblyVersion = typeof(Program).Assembly.GetName().Version!;
        return $"v{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
    }
}
