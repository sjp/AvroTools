using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AvroTool.Commands;
using Moq;
using NUnit.Framework;
using SJP.Avro.Tools.Idl;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;
using AvroProtocol = Avro.Protocol;

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

    private const string SimpleTestProtocolJson = """
{
  "protocol": "TestProtocol",
  "types": [
    {
      "type": "record",
      "name": "TestRecord",
      "fields": [
        {
          "name": "FirstName",
          "type": "string"
        },
        {
          "name": "LastName",
          "type": "string"
        }
      ]
    }
  ],
  "messages": {}
}
""";

    private TemporaryDirectory _tempDir;
    private Mock<IAnsiConsole> _console;
    private Mock<IIdlToAvroTranslator> _idlTranslator;
    private CommandContext _commandContext;
    private IdlCommand _commandHandler;

    private IdlParseResult _parseResult;

    [SetUp]
    public void Setup()
    {
        _tempDir = new TemporaryDirectory();

        _console = new Mock<IAnsiConsole>(MockBehavior.Strict);
        _console.Setup(c => c.Write(It.IsAny<IRenderable>()));

        _parseResult = IdlParseResult.Protocol(AvroProtocol.Parse(SimpleTestProtocolJson));
        _idlTranslator = new Mock<IIdlToAvroTranslator>(MockBehavior.Strict);
        _idlTranslator
            .Setup(t => t.Translate(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _parseResult);

        _commandContext = new CommandContext([], Mock.Of<IRemainingArguments>(), "idl", null);

        _commandHandler = new IdlCommand(
            _console.Object,
            _idlTranslator.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _tempDir?.Dispose();
    }

    [Test]
    public async Task ExecuteAsync_GivenValidParameters_WritesExpectedOutput()
    {
        const string input = SimpleTestIdl;

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, input);

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var command = new IdlCommand.Settings
        {
            IdlFile = sourceFile.FullName,
            Overwrite = true,
            OutputDirectory = sourceDir,
        };
        var result = await _commandHandler.ExecuteAsync(_commandContext, command, default);
        var resultFileContents = await File.ReadAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "TestProtocol.avpr"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(resultFileContents, Is.EqualTo(SimpleTestProtocolJson).IgnoreLineEndingFormat);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenInvalidInput_ReturnsError()
    {
        const string input = "%";

        _idlTranslator
            .Setup(t => t.Translate(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("something went wrong"));

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, input);

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var command = new IdlCommand.Settings
        {
            IdlFile = sourceFile.FullName,
            Overwrite = true,
            OutputDirectory = sourceDir,
        };
        var result = await _commandHandler.ExecuteAsync(_commandContext, command, default);

        Assert.That(result, Is.Not.Zero);
    }

    [Test]
    public async Task ExecuteAsync_GivenOutputAlreadyExistsWithoutOverwrite_ReturnsError()
    {
        const string input = SimpleTestIdl;

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, input);

        // copy to ensure it already exists
        File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestProtocol.avpr"));

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var command = new IdlCommand.Settings
        {
            IdlFile = sourceFile.FullName,
            Overwrite = false,
            OutputDirectory = sourceDir,
        };
        var result = await _commandHandler.ExecuteAsync(_commandContext, command, default);

        Assert.That(result, Is.Not.Zero);
    }

    [Test]
    public async Task ExecuteAsync_GivenOutputAlreadyExistsWithOverwrite_Succeeds()
    {
        const string input = SimpleTestIdl;

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, input);

        // copy to ensure it already exists
        File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestProtocol.avpr"));

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var command = new IdlCommand.Settings
        {
            IdlFile = sourceFile.FullName,
            Overwrite = true,
            OutputDirectory = sourceDir,
        };
        var result = await _commandHandler.ExecuteAsync(_commandContext, command, default);

        Assert.That(result, Is.Zero);
    }

    [Test]
    public async Task ExecuteAsync_GivenMissingDirectory_ResolvesToCurrentDir()
    {
        const string input = SimpleTestIdl;

        var originalDir = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_tempDir.DirectoryPath);

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, input);

        // copy to ensure it already exists
        File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestProtocol.avpr"));

        // expect an error in overwriting if in the same dir
        var command = new IdlCommand.Settings
        {
            IdlFile = sourceFile.FullName,
            Overwrite = false,
            OutputDirectory = null,
        };
        var result = await _commandHandler.ExecuteAsync(_commandContext, command, default);

        // restore dir
        Directory.SetCurrentDirectory(originalDir);

        Assert.That(result, Is.Not.Zero);
    }

    [Test]
    public void Validate_WithMissingInputFile_ReturnsError()
    {
        var settings = new IdlCommand.Settings
        {
            IdlFile = string.Empty
        };

        var result = _commandHandler.Validate(_commandContext, settings);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Successful, Is.False);
            Assert.That(result.Message, Is.EqualTo("An IDL file must be provided."));
        }
    }

    [Test]
    public void Validate_WithNonExistentInputFile_ReturnsError()
    {
        var settings = new IdlCommand.Settings
        {
            IdlFile = "a/b/c.avdl"
        };

        var result = _commandHandler.Validate(_commandContext, settings);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Successful, Is.False);
            Assert.That(result.Message, Is.EqualTo($"An IDL file could not be found at: {settings.IdlFile}"));
        }
    }

    [Test]
    public void Validate_WithValidParameters_ReturnsSuccess()
    {
        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        File.WriteAllText(sourceFile.FullName, SimpleTestIdl);

        var settings = new IdlCommand.Settings
        {
            IdlFile = sourceFile.FullName
        };

        var result = _commandHandler.Validate(_commandContext, settings);
        Assert.That(result.Successful, Is.True);
    }
}