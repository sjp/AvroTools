using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
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

    public static async Task<int> Main(string[] args)
    {
        UseUtf8Console();

        // Route status and diagnostic output to standard error so that standard
        // output carries only command payloads (e.g. 'idl --stdout'), keeping the
        // tool clean to use in shell pipelines. Help and version are payloads in
        // their own right, so they go to standard output as every other tool does.
        var statusConsole = CreateStatusConsole(Console.Error);
        var outputConsole = CreateOutputConsole(Console.Out);
        var helpConsole = CreateHelpConsole(Console.Out);

        // Declared before the registrar so that standard output is flushed after everything
        // that might still write to it has been torn down.
        using var streams = new ConsoleStandardStreams();

        var services = new ServiceCollection();
        RegisterServices(services, statusConsole, outputConsole, streams);
        using var registrar = new DependencyInjectionRegistrar(services);

        var app = new CommandApp(registrar);
        app.Configure(config => Configure(config, helpConsole, statusConsole, args));

        // A command is given the chance to unwind through its own cleanup — such as deleting a
        // half-written temporary file — rather than the runtime tearing the process down mid-write,
        // which is what the default SIGINT handling would otherwise do.
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        return await app.RunAsync(args, cancellation.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Asks the console to interpret the bytes written to it as UTF-8.
    /// </summary>
    /// <remarks>
    /// Payloads are written as UTF-8 whatever the platform. A console left on a legacy code page
    /// renders those bytes as mojibake and replaces anything it cannot represent with a question
    /// mark, so the code page is moved to match. Best effort: a process with no console attached
    /// has nothing to configure, and its output is already going somewhere that takes the bytes
    /// as they are.
    /// </remarks>
    private static void UseUtf8Console()
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// Creates the console that status and diagnostic messages are written to.
    /// </summary>
    /// <param name="writer">The writer the console renders to.</param>
    /// <returns>A console that renders to <paramref name="writer"/>.</returns>
    public static IStatusConsole CreateStatusConsole(TextWriter writer) =>
        new StatusConsole(CreateMessageConsole(writer));

    /// <summary>
    /// Creates the console that a command's human-readable payload is written to.
    /// </summary>
    /// <param name="writer">The writer the console renders to.</param>
    /// <returns>A console that renders to <paramref name="writer"/>.</returns>
    public static IOutputConsole CreateOutputConsole(TextWriter writer) =>
        new OutputConsole(CreateMessageConsole(writer));

    /// <summary>
    /// Creates a console for messages that are read a line at a time rather than laid out as a
    /// document.
    /// </summary>
    /// <param name="writer">The writer the console renders to.</param>
    /// <returns>A console that renders to <paramref name="writer"/>.</returns>
    private static IAnsiConsole CreateMessageConsole(TextWriter writer)
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
    /// Creates the console that help and version output are written to.
    /// </summary>
    /// <param name="writer">The writer the console renders to.</param>
    /// <returns>A console that renders to <paramref name="writer"/>.</returns>
    /// <remarks>
    /// Unlike status messages, help is a laid-out document of columns whose alignment is the
    /// point, so it keeps the default width rather than being rendered unwrapped.
    /// </remarks>
    public static IAnsiConsole CreateHelpConsole(TextWriter writer) =>
        AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Detect,
            ColorSystem = ColorSystemSupport.Detect,
            Out = new AnsiConsoleOutput(writer),
        });

    /// <summary>
    /// Registers the services the commands take as dependencies.
    /// </summary>
    /// <param name="services">The collection to register into.</param>
    /// <param name="statusConsole">The console status and diagnostic messages are written to.</param>
    /// <param name="outputConsole">The console human-readable payloads are written to.</param>
    /// <param name="streams">The streams command payloads are read from and written to.</param>
    public static void RegisterServices(IServiceCollection services, IStatusConsole statusConsole, IOutputConsole outputConsole, IStandardStreams streams)
    {
        services.AddSingleton(statusConsole);
        services.AddSingleton(outputConsole);
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
    /// <param name="helpConsole">The console help and version output are written to.</param>
    /// <param name="statusConsole">The console errors are written to.</param>
    /// <param name="args">The command line being run, which decides the exit code a failure reports.</param>
    public static void Configure(IConfigurator config, IAnsiConsole helpConsole, IStatusConsole statusConsole, IReadOnlyList<string> args)
    {
        config.SetApplicationName("avrotool");
        config.SetApplicationVersion(GetVersion());

        // Reject options that no command declares. Left lenient, a mistyped flag is
        // accepted in silence and swallows the argument that follows it, so a command
        // quietly does something other than what was asked for.
        config.UseStrictParsing();

        // Help and version are what the user asked for, so they belong on standard output
        // where a pipe or a redirect can capture them. This is also the console the framework
        // injects into any command that asks for an IAnsiConsole, which is why status output
        // travels under its own interface instead.
        config.ConfigureConsole(helpConsole);

        foreach (var command in CommandCatalogue.Commands)
            command.Register(config);

        config.ValidateExamples();

        // A command line that cannot be parsed, an argument that cannot be converted and a
        // failed validation are all user errors, so they are reported as the message alone
        // and exit non-zero. A stack trace only helps with a fault in the tool itself, so it
        // is kept for exceptions that are not raised by the command-line framework. The code
        // comes from the command that was asked for, because for a comparison command a
        // failure has to be distinguishable from a negative answer.
        var failureCode = CommandCatalogue.FailureCodeFor(args);

        config.SetExceptionHandler((ex, _) =>
        {
            // The user interrupted the command (e.g. Ctrl+C) rather than the command failing,
            // so it is reported with the exit code a shell uses for a signal-terminated process
            // and without the message or stack trace a genuine failure would get.
            if (ex is OperationCanceledException)
                return ErrorCode.Interrupted;

            switch (ex)
            {
                case CommandAppException { Pretty: { } pretty }:
                    statusConsole.Write(pretty);
                    break;
                case CommandAppException:
                    statusConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
                    break;
                default:
                    statusConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
                    break;
            }

            return failureCode;
        });
    }

    private static string GetVersion()
    {
        var assemblyVersion = typeof(Program).Assembly.GetName().Version!;
        return $"v{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
    }
}
