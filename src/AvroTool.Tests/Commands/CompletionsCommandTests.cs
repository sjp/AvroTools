using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvroTool.Commands;
using AvroTool.Completions;
using Moq;
using NUnit.Framework;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Testing;
using Spectre.Console.Rendering;

namespace AvroTool.Tests.Commands;

[TestFixture]
internal class CompletionsCommandTests
{
    private CommandAppTester _app;
    private TestStandardStreams _streams;
    private Mock<IAnsiConsole> _console;

    private static IEnumerable<CompletionsCommand.ShellKind> Shells => Enum.GetValues<CompletionsCommand.ShellKind>();

    [SetUp]
    public void Setup()
    {
        _console = new Mock<IAnsiConsole>(MockBehavior.Loose);
        _console.Setup(c => c.Write(It.IsAny<IRenderable>()));
        _streams = new TestStandardStreams();

        var registrar = new FakeTypeRegistrar();
        registrar.RegisterInstance(typeof(CompletionsCommand), new CompletionsCommand(_console.Object, _streams));

        _app = new CommandAppTester(registrar);
        _app.SetDefaultCommand<CompletionsCommand>();
    }

    [Test]
    public async Task ExecuteAsync_GivenKnownShell_WritesScriptToStdout()
    {
        var result = await _app.RunAsync(["bash"], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(_streams.OutputText, Does.Contain("complete -F _avrotool_completions avrotool"));
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenUnknownShell_ReturnsError()
    {
        var result = await _app.RunAsync(["nonsense"], TestContext.CurrentContext.CancellationToken);

        Assert.That(result.ExitCode, Is.Not.Zero);
    }

    [Test]
    public void Generate_GivenBash_ProducesBashScript()
    {
        var script = CompletionScriptGenerator.Generate(CompletionsCommand.ShellKind.Bash);

        Assert.That(script, Does.Contain("complete -F _avrotool_completions avrotool"));
    }

    [Test]
    public void Generate_GivenZsh_ProducesZshScript()
    {
        var script = CompletionScriptGenerator.Generate(CompletionsCommand.ShellKind.Zsh);

        Assert.That(script, Does.Contain("#compdef avrotool"));
    }

    [Test]
    public void Generate_GivenFish_ProducesFishScript()
    {
        var script = CompletionScriptGenerator.Generate(CompletionsCommand.ShellKind.Fish);

        Assert.That(script, Does.Contain("complete -c avrotool"));
    }

    [Test]
    public void Generate_GivenPowerShell_ProducesPowerShellScript()
    {
        var script = CompletionScriptGenerator.Generate(CompletionsCommand.ShellKind.PowerShell);

        Assert.That(script, Does.Contain("Register-ArgumentCompleter"));
    }

    [Test]
    public void Generate_GivenAnyShell_ContainsEveryCommand([ValueSource(nameof(Shells))] CompletionsCommand.ShellKind shell)
    {
        var script = CompletionScriptGenerator.Generate(shell);

        using (Assert.EnterMultipleScope())
        {
            foreach (var command in CommandCatalogue.Commands)
                Assert.That(script, Does.Contain(command.Name), $"'{command.Name}' is missing from the {shell} script.");
        }
    }

    [Test]
    public void Generate_GivenAnyShell_ContainsEveryOptionOfEveryCommand([ValueSource(nameof(Shells))] CompletionsCommand.ShellKind shell)
    {
        var script = CompletionScriptGenerator.Generate(shell);

        using (Assert.EnterMultipleScope())
        {
            foreach (var command in CommandCatalogue.Commands)
            {
                foreach (var longName in LongOptionNames(command.SettingsType))
                    Assert.That(script, Does.Contain(longName), $"'--{longName}' of '{command.Name}' is missing from the {shell} script.");
            }
        }
    }

    [Test]
    public void Generate_GivenAnyShell_CompletesEnumeratedOptionValues([ValueSource(nameof(Shells))] CompletionsCommand.ShellKind shell)
    {
        var script = CompletionScriptGenerator.Generate(shell);

        using (Assert.EnterMultipleScope())
        {
            foreach (var mode in CompatCommand.SupportedModes)
                Assert.That(script, Does.Contain(mode));
            foreach (var algorithm in FingerprintCommand.SupportedAlgorithms)
                Assert.That(script, Does.Contain(algorithm));
            foreach (var format in FingerprintCommand.SupportedFormats)
                Assert.That(script, Does.Contain(format));
            foreach (var shellName in Enum.GetNames<CompletionsCommand.ShellKind>())
                Assert.That(script, Does.Contain(shellName.ToLowerInvariant()));
        }
    }

    [Test]
    public void Commands_GivenCommandTypesInAssembly_AreAllPresentInTheCatalogue()
    {
        var commandTypes = typeof(CompletionsCommand).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && typeof(ICommand).IsAssignableFrom(t))
            .ToList();

        var catalogued = CommandCatalogue.Commands.Select(c => c.SettingsType.DeclaringType).ToList();

        Assert.That(commandTypes, Is.EquivalentTo(catalogued));
    }

    [Test]
    public void Commands_GivenCatalogue_HasUniqueNames()
    {
        var names = CommandCatalogue.Commands.Select(c => c.Name).ToList();

        Assert.That(names, Is.Unique);
    }

    private static IEnumerable<string> LongOptionNames(Type settingsType)
        => settingsType
            .GetProperties()
            .Select(p => p.GetCustomAttributes(typeof(CommandOptionAttribute), false).Cast<CommandOptionAttribute>().FirstOrDefault())
            .Where(a => a is { IsHidden: false })
            .SelectMany(a => a!.LongNames);
}
