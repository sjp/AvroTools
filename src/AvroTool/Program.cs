using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using AvroTool.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using SJP.Avro.Tools;
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
        var services = new ServiceCollection();
        services.AddTransient<IIdlCompiler, IdlCompiler>();
        services.AddTransient<IIdlTokenizer, IdlTokenizer>();
        services.AddTransient<ICodeGeneratorResolver, CodeGeneratorResolver>();
        services.AddTransient<IFileProvider>(_ => new PhysicalFileProvider(Directory.GetCurrentDirectory()));
        using var registrar = new DependencyInjectionRegistrar(services);

        var app = new CommandApp(registrar);

        app.Configure(config =>
        {
            config.SetApplicationName("avrotool");
            config.SetApplicationVersion(GetVersion());

            config.AddCommand<IdlCommand>("idl").WithDescription("Generates a JSON protocol file from an Avro IDL file.");
            config.AddCommand<IdlToSchemataCommand>("idl2schemata").WithDescription("Extract JSON schemata of the types from an Avro IDL file.");
            config.AddCommand<CodeGenCommand>("codegen").WithDescription("Generates C# code for a given Avro IDL, protocol or schema.");

            config.PropagateExceptions();
            config.ValidateExamples();
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