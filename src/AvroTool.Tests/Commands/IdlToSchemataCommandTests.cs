using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AvroTool.Commands;
using Moq;
using NUnit.Framework;
using SJP.Avro.Tools.CodeGen;
using SJP.Avro.Tools.Idl;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Testing;
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

    private const string MultiRecordIdl = """
namespace TestNamespace;

record TestRecord {
    array<Datum> data;
}

record Datum {
    string? name;
    int datumId;
    PairVolume pairVolumes;
}

record PairVolume {
    double? negative1;
    double? negative2;
}
""";

    private const string MultiRecordSchema = """
{
  "type": "record",
  "name": "TestRecord",
  "namespace": "TestNamespace",
  "fields": [
    {
      "name": "data",
      "type": {
        "type": "array",
        "items": {
          "type": "record",
          "name": "Datum",
          "namespace": "TestNamespace",
          "fields": [
            {
              "name": "name",
              "type": [
                "null",
                "string"
              ]
            },
            {
              "name": "datumId",
              "type": "int"
            },
            {
              "name": "pairVolumes",
              "type": {
                "type": "record",
                "name": "PairVolume",
                "namespace": "TestNamespace",
                "fields": [
                  {
                    "name": "negative1",
                    "type": [
                      "null",
                      "double"
                    ]
                  },
                  {
                    "name": "negative2",
                    "type": [
                      "null",
                      "double"
                    ]
                  }
                ]
              }
            }
          ]
        }
      }
    }
  ]
}
""";

    private CommandAppTester _app;
    private TemporaryDirectory _tempDir;
    private Mock<IAnsiConsole> _console;
    private Mock<IIdlToAvroTranslator> _idlTranslator;

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

        var registrar = new FakeTypeRegistrar();
        var command = new IdlToSchemataCommand(
            _console.Object,
            _idlTranslator.Object);
        registrar.RegisterInstance(typeof(IdlToSchemataCommand), command);

        _app = new CommandAppTester(registrar);
        _app.SetDefaultCommand<IdlToSchemataCommand>();
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
        var result = await _app.RunAsync([sourceFile.FullName, "--overwrite", "--output-dir", sourceDir.FullName], default);
        var resultFileContents = await File.ReadAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "TestRecord.avsc"));

        var expectedResultFileContents = _parseResult.Match(
            p => JsonNode.Parse(p.ToString()).ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            s => JsonNode.Parse(s.ToString()).ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(resultFileContents, Is.EqualTo(expectedResultFileContents).IgnoreLineEndingFormat);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenMultiRecordInput_WritesExpectedOutput()
    {
        const string input = MultiRecordIdl;

        _parseResult = IdlParseResult.Schema(AvroSchema.Parse(MultiRecordSchema));

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_multi_record_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, input);

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var result = await _app.RunAsync([sourceFile.FullName, "--overwrite", "--output-dir", sourceDir.FullName], default);

        var schemaCount = sourceDir.GetFiles("*.avsc");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(schemaCount, Has.Exactly(3).Items);
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
        var result = await _app.RunAsync([sourceFile.FullName, "--overwrite", "--output-dir", sourceDir.FullName], default);

        Assert.That(result.ExitCode, Is.Not.Zero);
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
        var result = await _app.RunAsync([sourceFile.FullName, "--output-dir", sourceDir.FullName], default);

        Assert.That(result.ExitCode, Is.Not.Zero);
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
        var result = await _app.RunAsync([sourceFile.FullName, "--overwrite", "--output-dir", sourceDir.FullName], default);

        Assert.That(result.ExitCode, Is.Zero);
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
        var result = await _app.RunAsync([sourceFile.FullName], default);

        // restore dir
        Directory.SetCurrentDirectory(originalDir);

        Assert.That(result.ExitCode, Is.Not.Zero);
    }

    [Test]
    public async Task Validate_WithMissingInputFile_ReturnsError()
    {
        var result = await _app.RunAsync([string.Empty], default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain("An IDL file must be provided."));
        }
    }

    [Test]
    public async Task Validate_WithNonExistentInputFile_ReturnsError()
    {
        const string IdlFile = "a/b/c.avdl";

        var result = await _app.RunAsync([IdlFile], default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain($"An IDL file could not be found at: {IdlFile}"));
        }
    }

    [Test]
    public async Task Validate_WithValidParameters_ReturnsSuccess()
    {
        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, SimpleTestIdl);

        var result = await _app.RunAsync([sourceFile.FullName], default);

        Assert.That(result.Output, Is.Empty);
    }
}