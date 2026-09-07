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
    private Mock<IStatusConsole> _console;

    private static IEnumerable<CompletionsCommand.ShellKind> Shells => Enum.GetValues<CompletionsCommand.ShellKind>();

    // bash, zsh and fish all treat a carriage return as part of the line, so their scripts must
    // be LF-terminated whatever platform generated them.
    private static IEnumerable<CompletionsCommand.ShellKind> PosixShells =>
    [
        CompletionsCommand.ShellKind.Bash,
        CompletionsCommand.ShellKind.Zsh,
        CompletionsCommand.ShellKind.Fish,
    ];

    [SetUp]
    public void Setup()
    {
        _console = new Mock<IStatusConsole>(MockBehavior.Loose);
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
                foreach (var flag in OptionFlags(command.SettingsType))
                    Assert.That(script, Does.Contain(FlagToken(shell, flag)), $"'{flag}' of '{command.Name}' is missing from the {shell} script.");
            }
        }
    }

    [Test]
    public void Generate_GivenPosixShell_UsesLineFeedsOnly([ValueSource(nameof(PosixShells))] CompletionsCommand.ShellKind shell)
    {
        var script = CompletionScriptGenerator.Generate(shell);

        Assert.That(script, Does.Not.Contain("\r"));
    }

    [Test]
    public void Generate_GivenZsh_DescribesOptionsAndGroupsTheirSpellings()
    {
        var script = CompletionScriptGenerator.Generate(CompletionsCommand.ShellKind.Zsh);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(script, Does.Contain("'(-a --algorithm)'{-a,--algorithm}'[The fingerprint algorithm: crc-64-avro (default), md5 or sha-256]:algorithm:(crc-64-avro md5 sha-256)'"));
            Assert.That(script, Does.Contain("'--stdin[Read the input from standard input instead of a file]'"));
            Assert.That(script, Does.Not.Contain(".]"), "A description kept the trailing full stop that a completion menu does not want.");
        }
    }

    [Test]
    public void Generate_GivenFish_DescribesOptionsAndOffersOnlyTheAcceptedValues()
    {
        var script = CompletionScriptGenerator.Generate(CompletionsCommand.ShellKind.Fish);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(script, Does.Contain("-l 'algorithm' -x -a 'crc-64-avro md5 sha-256' -d 'The fingerprint algorithm: crc-64-avro (default), md5 or sha-256'"));
            Assert.That(script, Does.Contain("-l 'output-dir' -x -a '(__fish_complete_directories)'"));
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

    private static IEnumerable<string> OptionFlags(Type settingsType)
        => settingsType
            .GetProperties()
            .Select(p => p.GetCustomAttributes(typeof(CommandOptionAttribute), false).Cast<CommandOptionAttribute>().FirstOrDefault())
            .Where(a => a is { IsHidden: false })
            .SelectMany(a => a!.ShortNames.Select(n => "-" + n).Concat(a.LongNames.Select(n => "--" + n)));

    // How a script spells a flag: bash, zsh and PowerShell write it as the user types it, while
    // fish splits it into a kind ('-s' for short, '-l' for long) and the name without dashes.
    private static string FlagToken(CompletionsCommand.ShellKind shell, string flag)
    {
        if (shell != CompletionsCommand.ShellKind.Fish)
            return flag;

        var kind = flag.StartsWith("--", StringComparison.Ordinal) ? "-l" : "-s";

        return $"{kind} '{flag.TrimStart('-')}'";
    }
}
