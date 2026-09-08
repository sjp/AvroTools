using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AvroTool.Commands;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SJP.Avro.Tools.CodeGen;
using SJP.Avro.Tools.Idl;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Testing;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;
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
    private TestStandardStreams _streams;
    private Mock<IStatusConsole> _console;
    private Mock<IIdlToAvroTranslator> _idlTranslator;

    private IdlParseResult _parseResult;

    [SetUp]
    public void Setup()
    {
        _tempDir = new TemporaryDirectory();

        _console = new Mock<IStatusConsole>(MockBehavior.Strict);
        _console.Setup(c => c.Write(It.IsAny<IRenderable>()));

        _parseResult = IdlParseResult.Schema(AvroSchema.Parse(SimpleTestAvroSchema));
        _idlTranslator = new Mock<IIdlToAvroTranslator>(MockBehavior.Strict);
        _idlTranslator
            .Setup(t => t.Translate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _parseResult);

        _streams = new TestStandardStreams();

        var registrar = new FakeTypeRegistrar();
        var command = new IdlToSchemataCommand(
            _console.Object,
            _streams,
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
        await File.WriteAllTextAsync(sourceFile.FullName, input, TestContext.CurrentContext.CancellationToken);

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var result = await _app.RunAsync([sourceFile.FullName, "--overwrite", "--output-dir", sourceDir.FullName], TestContext.CurrentContext.CancellationToken);
        var resultFileContents = await File.ReadAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "TestRecord.avsc"), TestContext.CurrentContext.CancellationToken);

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
        await File.WriteAllTextAsync(sourceFile.FullName, input, TestContext.CurrentContext.CancellationToken);

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var result = await _app.RunAsync([sourceFile.FullName, "--overwrite", "--output-dir", sourceDir.FullName], TestContext.CurrentContext.CancellationToken);

        var schemaCount = sourceDir.GetFiles("*.avsc");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(schemaCount, Has.Exactly(3).Items);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenARecordReferencingAnotherDeclaredType_WritesEachSchemaSelfContainedWithItsProperties()
    {
        const string input = """
protocol TestProtocol {
  record Datum {
    int @order("descending") value;
  }

  record TestRecord {
    Datum datum;
  }
}
""";

        // built the way the real translator does: a field's type is a bare reference, resolved
        // against a separate table of every named type the document declares, so that a property
        // the Avro object model would not preserve (a field's "order") still has to be recovered
        // from that table when each type is written out on its own.
        var datumJson = JObject.Parse("""
            {
              "type": "record",
              "name": "Datum",
              "namespace": "TestNamespace",
              "fields": [ { "name": "value", "type": "int", "order": "descending" } ]
            }
            """);
        var testRecordJson = JObject.Parse("""
            {
              "type": "record",
              "name": "TestRecord",
              "namespace": "TestNamespace",
              "fields": [ { "name": "datum", "type": "Datum" } ]
            }
            """);
        var namedSchemas = new Dictionary<string, JObject>
        {
            ["TestNamespace.Datum"] = datumJson,
            ["TestNamespace.TestRecord"] = testRecordJson,
        };

        // the parsed schema itself is not consulted by idl2schemata; only the raw JSON and the
        // named-type table are, so a trivial placeholder is enough to satisfy the constructor.
        _parseResult = IdlParseResult.Schema(AvroSchema.Parse(SimpleTestAvroSchema), testRecordJson, namedSchemas);

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, input, TestContext.CurrentContext.CancellationToken);

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var result = await _app.RunAsync([sourceFile.FullName, "--overwrite", "--output-dir", sourceDir.FullName], TestContext.CurrentContext.CancellationToken);

        var datumFileContents = await File.ReadAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "TestNamespace.Datum.avsc"), TestContext.CurrentContext.CancellationToken);
        var testRecordFileContents = await File.ReadAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "TestNamespace.TestRecord.avsc"), TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(datumFileContents, Does.Contain("\"order\": \"descending\""));

            // TestRecord.avsc must be usable on its own, so its "datum" field must carry the full
            // Datum definition rather than a bare name nothing in the file defines.
            Assert.That(testRecordFileContents, Does.Contain("\"name\": \"Datum\""));
            Assert.That(testRecordFileContents, Does.Contain("\"order\": \"descending\""));
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenInvalidInput_ReturnsError()
    {
        const string input = "%";

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, input, TestContext.CurrentContext.CancellationToken);

        _idlTranslator
            .Setup(t => t.Translate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("something went wrong"));

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var result = await _app.RunAsync([sourceFile.FullName, "--overwrite", "--output-dir", sourceDir.FullName], TestContext.CurrentContext.CancellationToken);

        Assert.That(result.ExitCode, Is.Not.Zero);
    }

    [Test]
    public async Task ExecuteAsync_GivenOutputAlreadyExistsWithoutOverwrite_ReturnsError()
    {
        const string input = SimpleTestIdl;

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, input, TestContext.CurrentContext.CancellationToken);

        // copy to ensure it already exists
        File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestRecord.avsc"));

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var result = await _app.RunAsync([sourceFile.FullName, "--output-dir", sourceDir.FullName], TestContext.CurrentContext.CancellationToken);

        Assert.That(result.ExitCode, Is.Not.Zero);
    }

    [Test]
    public async Task ExecuteAsync_GivenOutputAlreadyExistsWithOverwrite_Succeeds()
    {
        const string input = SimpleTestIdl;

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, input, TestContext.CurrentContext.CancellationToken);

        // copy to ensure it already exists
        File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestRecord.avsc"));

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var result = await _app.RunAsync([sourceFile.FullName, "--overwrite", "--output-dir", sourceDir.FullName], TestContext.CurrentContext.CancellationToken);

        Assert.That(result.ExitCode, Is.Zero);
    }

    [Test]
    public async Task ExecuteAsync_GivenMissingDirectory_ResolvesToCurrentDir()
    {
        const string input = SimpleTestIdl;

        var originalDir = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_tempDir.DirectoryPath);

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, input, TestContext.CurrentContext.CancellationToken);

        // copy to ensure it already exists
        File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestRecord.avsc"));

        // expect an error in overwriting if in the same dir
        var result = await _app.RunAsync([sourceFile.FullName], TestContext.CurrentContext.CancellationToken);

        // restore dir
        Directory.SetCurrentDirectory(originalDir);

        Assert.That(result.ExitCode, Is.Not.Zero);
    }

    [Test]
    public async Task Validate_WithStandardInputAndPositionalInput_ReturnsError()
    {
        var result = await _app.RunAsync(["--stdin", "a/b/c.avdl"], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain("IDL files may not be given together with --stdin."));
        }
    }

    [Test]
    public async Task Validate_WithMissingInputFile_ReturnsError()
    {
        var result = await _app.RunAsync([string.Empty], TestContext.CurrentContext.CancellationToken);

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

        var result = await _app.RunAsync([IdlFile], TestContext.CurrentContext.CancellationToken);

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
        await File.WriteAllTextAsync(sourceFile.FullName, SimpleTestIdl, TestContext.CurrentContext.CancellationToken);

        var result = await _app.RunAsync([sourceFile.FullName], TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Output, Is.Empty);
    }

    [Test]
    public async Task ExecuteAsync_GivenTwoInputsSharingAnImport_WritesTheSharedTypeOnceAndBothOwnTypes()
    {
        var app = CreateAppWithRealParsers();

        var sharedDir = Directory.CreateDirectory(Path.Combine(_tempDir.DirectoryPath, "shared"));
        await File.WriteAllTextAsync(
            Path.Combine(sharedDir.FullName, "common.avdl"),
            "protocol Shared { record SharedRecord { int s; } }",
            TestContext.CurrentContext.CancellationToken);

        var a = Path.Combine(_tempDir.DirectoryPath, "a.avdl");
        var b = Path.Combine(_tempDir.DirectoryPath, "b.avdl");
        await File.WriteAllTextAsync(a, """protocol A { import idl "shared/common.avdl"; record RA { SharedRecord s; } }""", TestContext.CurrentContext.CancellationToken);
        await File.WriteAllTextAsync(b, """protocol B { import idl "shared/common.avdl"; record RB { SharedRecord s; } }""", TestContext.CurrentContext.CancellationToken);

        var outputDir = Directory.CreateDirectory(Path.Combine(_tempDir.DirectoryPath, "out"));

        var result = await app.RunAsync([a, b, "--output-dir", outputDir.FullName], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(File.Exists(Path.Combine(outputDir.FullName, "SharedRecord.avsc")), Is.True);
            Assert.That(File.Exists(Path.Combine(outputDir.FullName, "RA.avsc")), Is.True);
            Assert.That(File.Exists(Path.Combine(outputDir.FullName, "RB.avsc")), Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenTwoInputsDefiningTheSameTypeDifferently_ReportsTheConflict()
    {
        var app = CreateAppWithRealParsers();

        var a = Path.Combine(_tempDir.DirectoryPath, "a.avdl");
        var b = Path.Combine(_tempDir.DirectoryPath, "b.avdl");
        await File.WriteAllTextAsync(a, "protocol A { record Same { int x; } record OnlyA { int a; } }", TestContext.CurrentContext.CancellationToken);
        await File.WriteAllTextAsync(b, "protocol B { record Same { string y; } record OnlyB { int b; } }", TestContext.CurrentContext.CancellationToken);

        var outputDir = Directory.CreateDirectory(Path.Combine(_tempDir.DirectoryPath, "out"));

        var result = await app.RunAsync([a, b, "--output-dir", outputDir.FullName], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(File.Exists(Path.Combine(outputDir.FullName, "OnlyA.avsc")), Is.True);
            Assert.That(File.Exists(Path.Combine(outputDir.FullName, "OnlyB.avsc")), Is.False);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenUnreadableInputAlongsideValidOne_ReportsItAndWritesTheOther()
    {
        var console = new TestConsole().Width(200);
        var registrar = new FakeTypeRegistrar();
        var translator = new IdlToAvroTranslator(new PhysicalIdlFileReader());
        registrar.RegisterInstance(typeof(IdlToSchemataCommand), new IdlToSchemataCommand(new StatusConsole(console), _streams, translator));

        var app = new CommandAppTester(registrar);
        app.SetDefaultCommand<IdlToSchemataCommand>();

        var good = Path.Combine(_tempDir.DirectoryPath, "good.avdl");
        await File.WriteAllTextAsync(good, SimpleTestIdl, TestContext.CurrentContext.CancellationToken);

        using var unreadable = new UnreadableFile(Path.Combine(_tempDir.DirectoryPath, "locked.avdl"));

        var outputDir = Directory.CreateDirectory(Path.Combine(_tempDir.DirectoryPath, "out"));

        var result = await app.RunAsync([unreadable.Path, good, "--output-dir", outputDir.FullName], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(console.Output, Does.Contain($"Unable to read '{unreadable.Path}'"));
            Assert.That(File.Exists(Path.Combine(outputDir.FullName, "TestRecord.avsc")), Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenFailFastAndUnreadableFirstInput_DoesNotProcessRest()
    {
        var app = CreateAppWithRealParsers();

        var good = Path.Combine(_tempDir.DirectoryPath, "2_good.avdl");
        await File.WriteAllTextAsync(good, SimpleTestIdl, TestContext.CurrentContext.CancellationToken);

        using var unreadable = new UnreadableFile(Path.Combine(_tempDir.DirectoryPath, "1_locked.avdl"));

        var outputDir = Directory.CreateDirectory(Path.Combine(_tempDir.DirectoryPath, "out"));

        // Ordinal ordering within the directory means the unreadable file comes first.
        var glob = Path.Combine(_tempDir.DirectoryPath, "*.avdl");
        var result = await app.RunAsync([glob, "--fail-fast", "--output-dir", outputDir.FullName], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(File.Exists(Path.Combine(outputDir.FullName, "TestRecord.avsc")), Is.False);
        }
    }

    /// <summary>
    /// An app wired to the real IDL translator, for the cases where the behaviour under test
    /// depends on what an actual document translates to.
    /// </summary>
    private CommandAppTester CreateAppWithRealParsers()
    {
        var registrar = new FakeTypeRegistrar();
        var translator = new IdlToAvroTranslator(new PhysicalIdlFileReader());
        registrar.RegisterInstance(typeof(IdlToSchemataCommand), new IdlToSchemataCommand(_console.Object, _streams, translator));

        var app = new CommandAppTester(registrar);
        app.SetDefaultCommand<IdlToSchemataCommand>();

        return app;
    }

    [Test]
    public async Task ExecuteAsync_GivenUntranslatableInput_ReportsTheFailureAndWritesNoSchemas()
    {
        var console = new TestConsole().Width(200);
        var registrar = new FakeTypeRegistrar();
        registrar.RegisterInstance(typeof(IdlToSchemataCommand), new IdlToSchemataCommand(new StatusConsole(console), _streams, _idlTranslator.Object));

        var app = new CommandAppTester(registrar);
        app.SetDefaultCommand<IdlToSchemataCommand>();

        _idlTranslator
            .Setup(t => t.Translate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("something went wrong"));

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, SimpleTestIdl, TestContext.CurrentContext.CancellationToken);

        var result = await app.RunAsync([sourceFile.FullName, "--output-dir", _tempDir.DirectoryPath], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(console.Output, Does.Contain("Unable to parse IDL document"));
            Assert.That(console.Output, Does.Contain("something went wrong"));
            Assert.That(File.Exists(Path.Combine(_tempDir.DirectoryPath, "TestRecord.avsc")), Is.False);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenOutputDirectoryThatDoesNotExist_CreatesItAndWritesOutput()
    {
        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, SimpleTestIdl, TestContext.CurrentContext.CancellationToken);

        var outputDir = Path.Combine(_tempDir.DirectoryPath, "does", "not", "exist");
        var result = await _app.RunAsync([sourceFile.FullName, "--output-dir", outputDir], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(File.Exists(Path.Combine(outputDir, "TestRecord.avsc")), Is.True);
        }
    }
}