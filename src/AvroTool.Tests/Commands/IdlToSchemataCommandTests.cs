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
using AvroSchema = Avro.Schema;

namespace AvroTool.Tests.Commands;

[TestFixture]
internal class IdlToSchemataCommandTests
{
    private const string SimpleTestIdl = @"protocol TestProtocol {
  record TestRecord {
    string FirstName;
    string LastName;
  }
}
";

    private const string SimpleTestAvroSchema = """
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
        """;

    private TemporaryDirectory _tempDir;
    private Mock<IAnsiConsole> _console;
    private Mock<IIdlToAvroTranslator> _idlTranslator;
    private CommandContext _commandContext;
    private IdlToSchemataCommand _commandHandler;

    private IdlParseResult _parseResult;

    [SetUp]
    public void Setup()
    {
        _tempDir = new TemporaryDirectory();

        _console = new Mock<IAnsiConsole>(MockBehavior.Strict);
        _console.Setup(c => c.Write(It.IsAny<IRenderable>()));

        _parseResult = IdlParseResult.Schema(AvroSchema.Parse(SimpleTestAvroSchema));
        _idlTranslator = new Mock<IIdlToAvroTranslator>(MockBehavior.Strict);
        _idlTranslator
            .Setup(t => t.Translate(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _parseResult);

        _commandContext = new CommandContext([], Mock.Of<IRemainingArguments>(), "idltoschemata", null);

        _commandHandler = new IdlToSchemataCommand(
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
        var command = new IdlToSchemataCommand.Settings
        {
            IdlFile = sourceFile.FullName,
            Overwrite = true,
            OutputDirectory = sourceDir
        };
        var result = await _commandHandler.ExecuteAsync(_commandContext, command, default);
        var resultFileContents = await File.ReadAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "TestRecord.avsc"));

        var expectedResultFileContents = _parseResult.Match(p => p.ToString(), s => s.ToString());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(resultFileContents, Is.EqualTo(expectedResultFileContents).IgnoreLineEndingFormat);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenInvalidInput_ReturnsError()
    {
        const string input = "%";

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, input);

        _idlTranslator
            .Setup(t => t.Translate(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("something went wrong"));

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var command = new IdlToSchemataCommand.Settings
        {
            IdlFile = sourceFile.FullName,
            Overwrite = true,
            OutputDirectory = sourceDir
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
        File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestRecord.avsc"));

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var command = new IdlToSchemataCommand.Settings
        {
            IdlFile = sourceFile.FullName,
            Overwrite = false,
            OutputDirectory = sourceDir
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
        File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestRecord.avsc"));

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var command = new IdlToSchemataCommand.Settings
        {
            IdlFile = sourceFile.FullName,
            Overwrite = true,
            OutputDirectory = sourceDir
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
        File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestRecord.avsc"));

        // expect an error in overwriting if in the same dir
        var command = new IdlToSchemataCommand.Settings
        {
            IdlFile = sourceFile.FullName,
            Overwrite = false,
            OutputDirectory = null
        };
        var result = await _commandHandler.ExecuteAsync(_commandContext, command, default);

        // restore dir
        Directory.SetCurrentDirectory(originalDir);

        Assert.That(result, Is.Not.Zero);
    }

    [Test]
    public void Validate_WithMissingInputFile_ReturnsError()
    {
        var settings = new IdlToSchemataCommand.Settings
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
        var settings = new IdlToSchemataCommand.Settings
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

        var settings = new IdlToSchemataCommand.Settings
        {
            IdlFile = sourceFile.FullName
        };

        var result = _commandHandler.Validate(_commandContext, settings);
        Assert.That(result.Successful, Is.True);
    }
}