using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AvroTool.Commands;
using Moq;
using NUnit.Framework;
using SJP.Avro.Tools;
using SJP.Avro.Tools.Idl;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace AvroTool.Tests.Commands;

[TestFixture]
internal class IdlCommandTests
{
    private const string SimpleTestIdl = @"protocol TestProtocol {
  record TestRecord {
    string FirstName;
    string LastName;
  }
}
";

    private TemporaryDirectory _tempDir;
    private Mock<IAnsiConsole> _console;
    private CommandContext _commandContext;
    private IdlCommand _commandHandler;

    [SetUp]
    public void Setup()
    {
        _tempDir = new TemporaryDirectory();

        _console = new Mock<IAnsiConsole>(MockBehavior.Strict);
        _console.Setup(c => c.Write(It.IsAny<IRenderable>()));

        _commandContext = new CommandContext([], Mock.Of<IRemainingArguments>(), "idl", null);

        _commandHandler = new IdlCommand(
            _console.Object,
            new IdlTokenizer(),
            new IdlCompiler(new DefaultFileProvider())
        );
    }

    [TearDown]
    public void TearDown()
    {
        _tempDir?.Dispose();
    }

    [Test]
    public async Task HandleAsync_GivenValidParameters_WritesExpectedOutput()
    {
        const string input = SimpleTestIdl;

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        File.WriteAllText(sourceFile.FullName, input);

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var command = new IdlCommand.Settings
        {
            IdlFile = sourceFile.FullName,
            Overwrite = true,
            OutputDirectory = sourceDir,
        };
        var result = await _commandHandler.ExecuteAsync(_commandContext, command).ConfigureAwait(false);
        var resultFileContents = await File.ReadAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "TestProtocol.avpr")).ConfigureAwait(false);

        const string expectedResultFileContents = @"{
  ""protocol"": ""TestProtocol"",
  ""types"": [
    {
      ""type"": ""record"",
      ""fields"": [
        {
          ""name"": ""FirstName"",
          ""type"": ""string""
        },
        {
          ""name"": ""LastName"",
          ""type"": ""string""
        }
      ],
      ""name"": ""TestRecord""
    }
  ],
  ""messages"": {}
}";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(resultFileContents, Is.EqualTo(expectedResultFileContents).IgnoreLineEndingFormat);
        }
    }

    [Test]
    public async Task HandleAsync_GivenMissingFile_ReturnsError()
    {
        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var command = new IdlCommand.Settings
        {
            IdlFile = sourceFile.FullName,
            Overwrite = true,
            OutputDirectory = sourceDir,
        };
        var result = await _commandHandler.ExecuteAsync(_commandContext, command).ConfigureAwait(false);

        Assert.That(result, Is.Not.Zero);
    }

    [Test]
    public async Task HandleAsync_GivenInvalidTokens_ReturnsError()
    {
        const string input = "%";

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        File.WriteAllText(sourceFile.FullName, input);

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var command = new IdlCommand.Settings
        {
            IdlFile = sourceFile.FullName,
            Overwrite = true,
            OutputDirectory = sourceDir,
        };
        var result = await _commandHandler.ExecuteAsync(_commandContext, command).ConfigureAwait(false);

        Assert.That(result, Is.Not.Zero);
    }

    [Test]
    public async Task HandleAsync_GivenValidIdlTokensButInvalidProtocol_ReturnsError()
    {
        const string input = @"record Foo {{
    string label;
}}";

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        File.WriteAllText(sourceFile.FullName, input);

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var command = new IdlCommand.Settings
        {
            IdlFile = sourceFile.FullName,
            Overwrite = true,
            OutputDirectory = sourceDir,
        };
        var result = await _commandHandler.ExecuteAsync(_commandContext, command).ConfigureAwait(false);

        Assert.That(result, Is.Not.Zero);
    }

    [Test]
    public async Task HandleAsync_GivenOutputAlreadyExistsWithoutOverwrite_ReturnsError()
    {
        const string input = SimpleTestIdl;

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        File.WriteAllText(sourceFile.FullName, input);

        // copy to ensure it already exists
        File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestProtocol.avpr"));

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var command = new IdlCommand.Settings
        {
            IdlFile = sourceFile.FullName,
            Overwrite = false,
            OutputDirectory = sourceDir,
        };
        var result = await _commandHandler.ExecuteAsync(_commandContext, command).ConfigureAwait(false);

        Assert.That(result, Is.Not.Zero);
    }

    [Test]
    public async Task HandleAsync_GivenOutputAlreadyExistsWithOverwrite_Succeeds()
    {
        const string input = SimpleTestIdl;

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        File.WriteAllText(sourceFile.FullName, input);

        // copy to ensure it already exists
        File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestProtocol.avpr"));

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var command = new IdlCommand.Settings
        {
            IdlFile = sourceFile.FullName,
            Overwrite = true,
            OutputDirectory = sourceDir,
        };
        var result = await _commandHandler.ExecuteAsync(_commandContext, command).ConfigureAwait(false);

        Assert.That(result, Is.Zero);
    }

    [Test]
    public async Task HandleAsync_GivenMissingDirectory_ResolvesToCurrentDir()
    {
        const string input = SimpleTestIdl;

        var originalDir = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_tempDir.DirectoryPath);

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        File.WriteAllText(sourceFile.FullName, input);

        // copy to ensure it already exists
        File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestProtocol.avpr"));

        // expect an error in overwriting if in the same dir
        var command = new IdlCommand.Settings
        {
            IdlFile = sourceFile.FullName,
            Overwrite = false,
            OutputDirectory = null,
        };
        var result = await _commandHandler.ExecuteAsync(_commandContext, command).ConfigureAwait(false);

        // restore dir
        Directory.SetCurrentDirectory(originalDir);

        Assert.That(result, Is.Not.Zero);
    }

    [Test]
    public async Task HandleAsync_GivenErrorInCompilation_ReturnsError()
    {
        var brokenCompiler = new Mock<IIdlCompiler>(MockBehavior.Strict);
        brokenCompiler
            .Setup(c => c.Compile(It.IsAny<string>(), It.IsAny<SJP.Avro.Tools.Idl.Model.Protocol>()))
            .Throws(new Exception("compiler failure"));

        _commandHandler = new IdlCommand(
            _console.Object,
            new IdlTokenizer(),
            brokenCompiler.Object
        );

        const string input = SimpleTestIdl;

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        File.WriteAllText(sourceFile.FullName, input);

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);

        var command = new IdlCommand.Settings
        {
            IdlFile = sourceFile.FullName,
            Overwrite = true,
            OutputDirectory = sourceDir,
        };
        var result = await _commandHandler.ExecuteAsync(_commandContext, command).ConfigureAwait(false);

        Assert.That(result, Is.Not.Zero);
    }
}