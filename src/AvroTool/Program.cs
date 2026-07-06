using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using AvroTool.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
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
        services.AddTransient<ICodeGeneratorResolver, CodeGeneratorResolver>();
        services.AddTransient<IFileProvider>(_ => new PhysicalFileProvider(Directory.GetCurrentDirectory()));
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

            config.AddCommand<IdlCommand>("idl").WithDescription("Generates a JSON protocol file from an Avro IDL file.");
            config.AddCommand<IdlToSchemataCommand>("idl2schemata").WithDescription("Extract JSON schemata of the types from an Avro IDL file.");
            config.AddCommand<CodeGenCommand>("codegen").WithDescription("Generates C# code for a given Avro IDL, protocol or schema.");
            config.AddCommand<CompatCommand>("compat").WithDescription("Checks whether two Avro schemas are compatible under Avro's schema-evolution rules.");
            config.AddCommand<DiffCommand>("diff").WithDescription("Prints a semantic diff between two Avro schemas.");
            config.AddCommand<CanonicalCommand>("canonical").WithDescription("Prints the Parsing Canonical Form of an Avro IDL, protocol or schema.");
            config.AddCommand<FingerprintCommand>("fingerprint").WithDescription("Computes a fingerprint (crc-64-avro, md5 or sha-256) of an Avro IDL, protocol or schema.");
            config.AddCommand<GetSchemaCommand>("getschema").WithDescription("Prints the writer schema embedded in an Avro object container file.");
            config.AddCommand<ToJsonCommand>("tojson").WithDescription("Decodes an Avro object container file's records to JSON.");
            config.AddCommand<CompletionsCommand>("completions")
                .WithDescription("Generates a shell completion script (bash, zsh, fish, powershell).")
                .WithExample("completions", "bash");

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