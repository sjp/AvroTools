using System.Threading.Tasks;
using AvroTool.Commands;
using AvroTool.Completions;
using Moq;
using NUnit.Framework;
using Spectre.Console;
using Spectre.Console.Cli.Testing;
using Spectre.Console.Rendering;

namespace AvroTool.Tests.Commands;

[TestFixture]
internal class CompletionsCommandTests
{
    private CommandAppTester _app;
    private Mock<IAnsiConsole> _console;

    [SetUp]
    public void Setup()
    {
        _console = new Mock<IAnsiConsole>(MockBehavior.Loose);
        _console.Setup(c => c.Write(It.IsAny<IRenderable>()));

        var registrar = new FakeTypeRegistrar();
        registrar.RegisterInstance(typeof(CompletionsCommand), new CompletionsCommand(_console.Object));

        _app = new CommandAppTester(registrar);
        _app.SetDefaultCommand<CompletionsCommand>();
    }

    [Test]
    public async Task ExecuteAsync_GivenKnownShell_ReturnsSuccess()
    {
        var result = await _app.RunAsync(["bash"], default);

        Assert.That(result.ExitCode, Is.Zero);
    }

    [Test]
    public async Task ExecuteAsync_GivenUnknownShell_ReturnsError()
    {
        var result = await _app.RunAsync(["nonsense"], default);

        Assert.That(result.ExitCode, Is.Not.Zero);
    }

    [Test]
    public void Generate_GivenBash_ProducesBashScript()
    {
        var script = CompletionScriptGenerator.Generate(CompletionsCommand.ShellKind.Bash);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(script, Does.Contain("complete -F _avrotool_completions avrotool"));
            AssertContainsCommandsAndFlags(script);
        }
    }

    [Test]
    public void Generate_GivenZsh_ProducesZshScript()
    {
        var script = CompletionScriptGenerator.Generate(CompletionsCommand.ShellKind.Zsh);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(script, Does.Contain("#compdef avrotool"));
            AssertContainsCommandsAndFlags(script);
        }
    }

    [Test]
    public void Generate_GivenFish_ProducesFishScript()
    {
        var script = CompletionScriptGenerator.Generate(CompletionsCommand.ShellKind.Fish);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(script, Does.Contain("complete -c avrotool"));
            AssertContainsCommandsAndFlags(script);
        }
    }

    [Test]
    public void Generate_GivenPowerShell_ProducesPowerShellScript()
    {
        var script = CompletionScriptGenerator.Generate(CompletionsCommand.ShellKind.PowerShell);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(script, Does.Contain("Register-ArgumentCompleter"));
            AssertContainsCommandsAndFlags(script);
        }
    }

    private static void AssertContainsCommandsAndFlags(string script)
    {
        Assert.That(script, Does.Contain("idl"));
        Assert.That(script, Does.Contain("idl2schemata"));
        Assert.That(script, Does.Contain("codegen"));
        Assert.That(script, Does.Contain("canonical"));
        Assert.That(script, Does.Contain("fingerprint"));
        Assert.That(script, Does.Contain("completions"));
        Assert.That(script, Does.Contain("overwrite"));
        Assert.That(script, Does.Contain("output-dir"));
    }
}
