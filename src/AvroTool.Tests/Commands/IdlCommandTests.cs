using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AvroTool.Commands;
using Moq;
using NUnit.Framework;
using SJP.Avro.Tools.Idl;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Testing;
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

        _parseResult = IdlParseResult.Protocol(AvroProtocol.Parse(SimpleTestProtocolJson));
        _idlTranslator = new Mock<IIdlToAvroTranslator>(MockBehavior.Strict);
        _idlTranslator
            .Setup(t => t.Translate(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _parseResult);

        var registrar = new FakeTypeRegistrar();
        var command = new IdlCommand(
            _console.Object,
            _idlTranslator.Object);
        registrar.RegisterInstance(typeof(IdlCommand), command);

        _app = new CommandAppTester(registrar);
        _app.SetDefaultCommand<IdlCommand>();
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
        var resultFileContents = await File.ReadAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "TestProtocol.avpr"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(resultFileContents, Is.EqualTo(SimpleTestProtocolJson).IgnoreLineEndingFormat);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenStdoutOption_WritesPayloadToStandardOutput()
    {
        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, SimpleTestIdl);

        var originalOut = Console.Out;
        var stdout = new StringWriter();
        Console.SetOut(stdout);
        try
        {
            var result = await _app.RunAsync([sourceFile.FullName, "--stdout"], default);

            var normalizedStdout = stdout.ToString().ReplaceLineEndings("\n");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.Zero);
                Assert.That(normalizedStdout, Does.Contain(SimpleTestProtocolJson.ReplaceLineEndings("\n")));
                // no file should be written when emitting to standard output
                Assert.That(File.Exists(Path.Combine(_tempDir.DirectoryPath, "TestProtocol.avpr")), Is.False);
            }
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenStdinInputAndStdoutOption_PipesInputToOutput()
    {
        var originalIn = Console.In;
        var originalOut = Console.Out;
        Console.SetIn(new StringReader(SimpleTestIdl));
        var stdout = new StringWriter();
        Console.SetOut(stdout);
        try
        {
            var result = await _app.RunAsync(["--stdin", "--stdout"], default);

            var normalizedStdout = stdout.ToString().ReplaceLineEndings("\n");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.Zero);
                Assert.That(normalizedStdout, Does.Contain(SimpleTestProtocolJson.ReplaceLineEndings("\n")));
            }
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenInvalidInput_ReturnsError()
    {
        const string input = "%";

        _idlTranslator
            .Setup(t => t.Translate(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("something went wrong"));

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, input);

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var result = await _app.RunAsync([sourceFile.FullName, "--overwrite", "--output-dir", sourceDir.FullName], default);

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
        File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestProtocol.avpr"));

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
        File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestProtocol.avpr"));

        // expect an error in overwriting if in the same dir
        var result = await _app.RunAsync([sourceFile.FullName], default);

        // restore dir
        Directory.SetCurrentDirectory(originalDir);

        Assert.That(result, Is.Not.Zero);
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

    private const string ProtocolOneJson = @"{""protocol"":""ProtocolOne"",""types"":[],""messages"":{}}";
    private const string ProtocolTwoJson = @"{""protocol"":""ProtocolTwo"",""types"":[],""messages"":{}}";

    private void SetupTranslatorToParseProtocolFromContent()
    {
        _idlTranslator
            .Setup(t => t.Translate(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string content, CancellationToken _) => IdlParseResult.Protocol(AvroProtocol.Parse(content)));
    }

    [Test]
    public async Task ExecuteAsync_GivenDirectoryInput_ProcessesContainedFiles()
    {
        var inputDir = new DirectoryInfo(Path.Combine(_tempDir.DirectoryPath, "inputs"));
        inputDir.Create();
        await File.WriteAllTextAsync(Path.Combine(inputDir.FullName, "input.avdl"), SimpleTestIdl);

        var outputDir = new DirectoryInfo(Path.Combine(_tempDir.DirectoryPath, "out"));
        outputDir.Create();

        var result = await _app.RunAsync([inputDir.FullName, "--output-dir", outputDir.FullName], default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(File.Exists(Path.Combine(outputDir.FullName, "TestProtocol.avpr")), Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenGlobInput_ProcessesMatchingFiles()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "input.avdl"), SimpleTestIdl);

        var outputDir = new DirectoryInfo(Path.Combine(_tempDir.DirectoryPath, "out"));
        outputDir.Create();

        var glob = Path.Combine(_tempDir.DirectoryPath, "*.avdl");
        var result = await _app.RunAsync([glob, "--output-dir", outputDir.FullName], default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(File.Exists(Path.Combine(outputDir.FullName, "TestProtocol.avpr")), Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenMultipleDistinctInputs_ProcessesAll()
    {
        SetupTranslatorToParseProtocolFromContent();

        var one = Path.Combine(_tempDir.DirectoryPath, "one.avdl");
        var two = Path.Combine(_tempDir.DirectoryPath, "two.avdl");
        await File.WriteAllTextAsync(one, ProtocolOneJson);
        await File.WriteAllTextAsync(two, ProtocolTwoJson);

        var outputDir = new DirectoryInfo(Path.Combine(_tempDir.DirectoryPath, "out"));
        outputDir.Create();

        var result = await _app.RunAsync([one, two, "--output-dir", outputDir.FullName], default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(File.Exists(Path.Combine(outputDir.FullName, "ProtocolOne.avpr")), Is.True);
            Assert.That(File.Exists(Path.Combine(outputDir.FullName, "ProtocolTwo.avpr")), Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenMultipleInputsProducingSameOutput_ReportsDuplicateAndFails()
    {
        // Both inputs translate to the same protocol name, so the second collides with the first.
        var one = Path.Combine(_tempDir.DirectoryPath, "one.avdl");
        var two = Path.Combine(_tempDir.DirectoryPath, "two.avdl");
        await File.WriteAllTextAsync(one, SimpleTestIdl);
        await File.WriteAllTextAsync(two, SimpleTestIdl);

        var outputDir = new DirectoryInfo(Path.Combine(_tempDir.DirectoryPath, "out"));
        outputDir.Create();

        var result = await _app.RunAsync([one, two, "--overwrite", "--output-dir", outputDir.FullName], default);

        Assert.That(result.ExitCode, Is.Not.Zero);
    }

    [Test]
    public async Task ExecuteAsync_GivenFailFastAndFailingFirstInput_DoesNotProcessRest()
    {
        _idlTranslator
            .Setup(t => t.Translate(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string content, CancellationToken _) =>
                content.Contains("BAD")
                    ? throw new InvalidOperationException("bad input")
                    : IdlParseResult.Protocol(AvroProtocol.Parse(content)));

        // Ordinal ordering within the directory means 1_bad.avdl is processed before 2_good.avdl.
        var bad = Path.Combine(_tempDir.DirectoryPath, "1_bad.avdl");
        var good = Path.Combine(_tempDir.DirectoryPath, "2_good.avdl");
        await File.WriteAllTextAsync(bad, "BAD");
        await File.WriteAllTextAsync(good, ProtocolOneJson);

        var outputDir = new DirectoryInfo(Path.Combine(_tempDir.DirectoryPath, "out"));
        outputDir.Create();

        var glob = Path.Combine(_tempDir.DirectoryPath, "*.avdl");
        var result = await _app.RunAsync([glob, "--fail-fast", "--output-dir", outputDir.FullName], default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(File.Exists(Path.Combine(outputDir.FullName, "ProtocolOne.avpr")), Is.False);
        }
    }
}