using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Extensions.DependencyInjection;
using Spectre.Console.Testing;

namespace AvroTool.Tests;

/// <summary>
/// Exercises the configuration the released tool runs with, rather than a configuration
/// assembled by a test, so that how the tool reports a bad command line is covered.
/// </summary>
[TestFixture]
internal sealed class ProgramTests
{
    /// <summary>A stack frame as rendered in an exception's stack trace.</summary>
    private const string StackFramePattern = @"(?m)^\s+at \S+\(";

    private sealed class ThrowingCommand : Command<ThrowingCommand.Settings>
    {
        public sealed class Settings : CommandSettings;

        protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken) =>
            throw new InvalidDataException("something went wrong inside the tool");
    }

    private static async Task<(int ExitCode, string Output, string ErrorOutput)> RunAsync(params string[] args)
    {
        var helpConsole = new TestConsole().Width(200);
        var statusConsole = new TestConsole().Width(200);

        var services = new ServiceCollection();
        Program.RegisterServices(services, new StatusConsole(statusConsole), new OutputConsole(new TestConsole()), new TestStandardStreams());
        services.AddTransient<ThrowingCommand>();

        using var registrar = new DependencyInjectionRegistrar(services);
        var app = new CommandApp(registrar);
        app.Configure(config =>
        {
            Program.Configure(config, helpConsole, new StatusConsole(statusConsole), args);
            config.AddCommand<ThrowingCommand>("throwing");
        });

        var exitCode = await app.RunAsync(args, TestContext.CurrentContext.CancellationToken);
        return (exitCode, helpConsole.Output, statusConsole.Output);
    }

    [Test]
    public void CreateStatusConsole_GivenWriterThatIsNotATerminal_WritesLongMessageOnOneLine()
    {
        var writer = new StringWriter();
        var console = Program.CreateStatusConsole(writer);
        var message = "Generated /home/user/" + new string('a', 100) + "/Protocol.avpr";

        console.MarkupLine(Markup.Escape(message));

        Assert.That(writer.ToString().TrimEnd(), Is.EqualTo(message));
    }

    [Test]
    public async Task RunAsync_GivenInputFileThatDoesNotExist_ReportsMessageWithoutStackTrace()
    {
        var (exitCode, _, errorOutput) = await RunAsync("idl", "does_not_exist.avdl");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(ErrorCode.Error));
            Assert.That(errorOutput, Does.Contain("An IDL file could not be found at: does_not_exist.avdl"));
            Assert.That(errorOutput, Does.Not.Match(StackFramePattern));
        }
    }

    [Test]
    public async Task RunAsync_GivenNoInputAtAll_ReportsMessageWithoutStackTrace()
    {
        var (exitCode, _, errorOutput) = await RunAsync("codegen");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(ErrorCode.Error));
            Assert.That(errorOutput, Does.Contain("An input file must be provided."));
            Assert.That(errorOutput, Does.Not.Match(StackFramePattern));
        }
    }

    [Test]
    public async Task RunAsync_GivenUnknownOptionValue_ReportsMessageWithoutStackTrace()
    {
        var (exitCode, _, errorOutput) = await RunAsync("fingerprint", "--algorithm", "nope", "does_not_exist.avsc");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(ErrorCode.Error));
            Assert.That(errorOutput, Does.Contain("Unknown algorithm 'nope'."));
            Assert.That(errorOutput, Does.Not.Match(StackFramePattern));
        }
    }

    [Test]
    public async Task RunAsync_GivenArgumentThatCannotBeConverted_ReportsMessageWithoutStackTrace()
    {
        var (exitCode, _, errorOutput) = await RunAsync("completions", "nope");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(ErrorCode.Error));
            Assert.That(errorOutput, Does.Contain("Failed to convert 'nope' to ShellKind."));
            Assert.That(errorOutput, Does.Not.Match(StackFramePattern));
        }
    }

    [Test]
    public async Task RunAsync_GivenUnknownCommand_ReportsMessageWithoutStackTrace()
    {
        var (exitCode, _, errorOutput) = await RunAsync("bogus");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(ErrorCode.Error));
            Assert.That(errorOutput, Does.Contain("Unknown command 'bogus'."));
            Assert.That(errorOutput, Does.Not.Match(StackFramePattern));
        }
    }

    [Test]
    public async Task RunAsync_GivenUnknownOptionOnSingleInputCommand_ReportsOptionAndFails()
    {
        var (exitCode, _, errorOutput) = await RunAsync("idl", "--bogus", "sample.avdl");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(ErrorCode.Error));
            Assert.That(errorOutput, Does.Contain("Unknown option 'bogus'."));
            Assert.That(errorOutput, Does.Not.Match(StackFramePattern));
        }
    }

    [Test]
    public async Task RunAsync_GivenUnknownOptionOnMultiInputCommand_ReportsOptionAndFails()
    {
        var (exitCode, _, errorOutput) = await RunAsync("codegen", "sample.avsc", "--requird");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(ErrorCode.Error));
            Assert.That(errorOutput, Does.Contain("Unknown option 'requird'."));
            Assert.That(errorOutput, Does.Not.Match(StackFramePattern));
        }
    }

    [Test]
    public async Task RunAsync_GivenUnknownOptionOnTwoSchemaCommand_ReportsOptionAndFails()
    {
        var (exitCode, _, errorOutput) = await RunAsync("compat", "reader.avsc", "writer.avsc", "--mdoe", "forward");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(ErrorCode.ComparisonError));
            Assert.That(errorOutput, Does.Contain("Unknown option 'mdoe'."));
            Assert.That(errorOutput, Does.Not.Match(StackFramePattern));
        }
    }

    [Test]
    public async Task RunAsync_GivenTooManyArguments_ReportsMessageWithoutStackTrace()
    {
        var (exitCode, _, errorOutput) = await RunAsync("canonical", "first.avsc", "second.avsc");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(ErrorCode.Error));
            Assert.That(errorOutput, Does.Contain("second.avsc"));
            Assert.That(errorOutput, Does.Not.Match(StackFramePattern));
        }
    }

    [Test]
    public async Task RunAsync_GivenCommandThatThrows_ReportsExceptionAndFails()
    {
        var (exitCode, _, errorOutput) = await RunAsync("throwing");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(ErrorCode.Error));
            Assert.That(errorOutput, Does.Contain("something went wrong inside the tool"));
        }
    }

    [Test]
    public async Task RunAsync_GivenHelpOption_WritesEveryCommandToStandardOutput()
    {
        var (exitCode, output, errorOutput) = await RunAsync("--help");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(ErrorCode.Success));
            foreach (var command in CommandCatalogue.Commands)
                Assert.That(output, Does.Contain(command.Name));
            Assert.That(errorOutput, Is.Empty);
        }
    }

    [Test]
    public async Task RunAsync_GivenCommandHelpOption_WritesHelpToStandardOutput()
    {
        var (exitCode, output, errorOutput) = await RunAsync("idl", "--help");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(ErrorCode.Success));
            Assert.That(output, Does.Contain("avrotool idl"));
            Assert.That(errorOutput, Is.Empty);
        }
    }

    [Test]
    public async Task RunAsync_GivenVersionOption_WritesToolVersionToStandardOutput()
    {
        var (exitCode, output, errorOutput) = await RunAsync("--version");

        var expectedVersion = typeof(Program).Assembly.GetName().Version!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(ErrorCode.Success));
            Assert.That(
                output.Trim(),
                Is.EqualTo($"v{expectedVersion.Major}.{expectedVersion.Minor}.{expectedVersion.Build}"));
            Assert.That(errorOutput, Is.Empty);
        }
    }

    [Test]
    public async Task RunAsync_GivenUnknownCommand_WritesNothingToStandardOutput()
    {
        var (exitCode, output, _) = await RunAsync("bogus");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(ErrorCode.Error));
            Assert.That(output, Is.Empty);
        }
    }

    [Test]
    public async Task RunAsync_GivenCompatWithUnknownMode_ExitsWithACodeDistinctFromANegativeResult()
    {
        var (exitCode, _, errorOutput) = await RunAsync("compat", "v2.avsc", "v1.avsc", "--mode", "sideways");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(ErrorCode.ComparisonError));
            Assert.That(exitCode, Is.Not.EqualTo(ErrorCode.Difference));
            Assert.That(errorOutput, Does.Contain("Unknown mode 'sideways'"));
        }
    }

    [Test]
    public async Task RunAsync_GivenDiffWithSchemaThatDoesNotExist_ExitsWithACodeDistinctFromANegativeResult()
    {
        var (exitCode, _, errorOutput) = await RunAsync("diff", "does_not_exist.avsc", "also_missing.avsc");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(ErrorCode.ComparisonError));
            Assert.That(exitCode, Is.Not.EqualTo(ErrorCode.Difference));
            Assert.That(errorOutput, Does.Contain("A schema file could not be found at: does_not_exist.avsc"));
        }
    }
}
