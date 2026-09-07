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
using Spectre.Console.Testing;
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

    private const string UnicodeTestProtocolJson = """
{
  "protocol": "TestProtocol",
  "doc": "Café <b>bold</b> & more, it's 1+1",
  "types": [
    {
      "type": "record",
      "name": "TestRecord",
      "doc": "日本語 & <markup>",
      "fields": [
        {
          "name": "FirstName",
          "type": "string",
          "default": "é<>&"
        }
      ]
    }
  ],
  "messages": {}
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

        _parseResult = IdlParseResult.Protocol(AvroProtocol.Parse(SimpleTestProtocolJson));
        _idlTranslator = new Mock<IIdlToAvroTranslator>(MockBehavior.Strict);
        _idlTranslator
            .Setup(t => t.Translate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _parseResult);

        _streams = new TestStandardStreams();

        var registrar = new FakeTypeRegistrar();
        var command = new IdlCommand(
            _console.Object,
            _streams,
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
        await File.WriteAllTextAsync(sourceFile.FullName, input, TestContext.CurrentContext.CancellationToken);

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var result = await _app.RunAsync([sourceFile.FullName, "--overwrite", "--output-dir", sourceDir.FullName], TestContext.CurrentContext.CancellationToken);
        var resultFileContents = await File.ReadAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "TestProtocol.avpr"), TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(resultFileContents, Is.EqualTo(SimpleTestProtocolJson).IgnoreLineEndingFormat);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenNonAsciiAndHtmlSensitiveText_WritesThemLiterally()
    {
        _parseResult = IdlParseResult.Protocol(AvroProtocol.Parse(UnicodeTestProtocolJson));

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, SimpleTestIdl, TestContext.CurrentContext.CancellationToken);

        var result = await _app.RunAsync([sourceFile.FullName, "--overwrite", "--output-dir", _tempDir.DirectoryPath], TestContext.CurrentContext.CancellationToken);
        var resultFileContents = await File.ReadAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "TestProtocol.avpr"), TestContext.CurrentContext.CancellationToken);

        // compared as text rather than parsed JSON, because the point is how the characters are written
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(resultFileContents, Does.Contain("Café <b>bold</b> & more, it's 1+1"));
            Assert.That(resultFileContents, Does.Contain("日本語 & <markup>"));
            Assert.That(resultFileContents, Does.Contain("\"default\": \"é<>&\""));
            Assert.That(resultFileContents, Does.Not.Contain("\\u"));
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenStdoutOption_WritesPayloadToStandardOutput()
    {
        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, SimpleTestIdl, TestContext.CurrentContext.CancellationToken);

        var result = await _app.RunAsync([sourceFile.FullName, "--stdout"], TestContext.CurrentContext.CancellationToken);

        var normalizedStdout = _streams.OutputText.ReplaceLineEndings("\n");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(normalizedStdout, Does.Contain(SimpleTestProtocolJson.ReplaceLineEndings("\n")));
            // no file should be written when emitting to standard output
            Assert.That(File.Exists(Path.Combine(_tempDir.DirectoryPath, "TestProtocol.avpr")), Is.False);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenStdinInputAndStdoutOption_PipesInputToOutput()
    {
        _streams.StandardInputText = SimpleTestIdl;

        var result = await _app.RunAsync(["--stdin", "--stdout"], TestContext.CurrentContext.CancellationToken);

        var normalizedStdout = _streams.OutputText.ReplaceLineEndings("\n");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(normalizedStdout, Does.Contain(SimpleTestProtocolJson.ReplaceLineEndings("\n")));
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenFileInput_ResolvesImportsAgainstTheFilesDirectory()
    {
        string capturedBaseDirectory = null;
        _idlTranslator
            .Setup(t => t.Translate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string baseDirectory, CancellationToken _) => capturedBaseDirectory = baseDirectory)
            .ReturnsAsync(() => _parseResult);

        var sourceDir = Directory.CreateDirectory(Path.Combine(_tempDir.DirectoryPath, "sub"));
        var sourceFile = new FileInfo(Path.Combine(sourceDir.FullName, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, SimpleTestIdl, TestContext.CurrentContext.CancellationToken);

        var result = await _app.RunAsync([sourceFile.FullName, "--output-dir", _tempDir.DirectoryPath], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(capturedBaseDirectory, Is.EqualTo(sourceDir.FullName));
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenStdinInput_ResolvesImportsAgainstTheCurrentDirectory()
    {
        string capturedBaseDirectory = null;
        _idlTranslator
            .Setup(t => t.Translate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string baseDirectory, CancellationToken _) => capturedBaseDirectory = baseDirectory)
            .ReturnsAsync(() => _parseResult);

        _streams.StandardInputText = SimpleTestIdl;

        var result = await _app.RunAsync(["--stdin", "--output-dir", _tempDir.DirectoryPath], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(capturedBaseDirectory, Is.EqualTo(Directory.GetCurrentDirectory()));
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenInvalidInput_ReturnsError()
    {
        const string input = "%";

        _idlTranslator
            .Setup(t => t.Translate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("something went wrong"));

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, input, TestContext.CurrentContext.CancellationToken);

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var result = await _app.RunAsync([sourceFile.FullName, "--overwrite", "--output-dir", sourceDir.FullName], TestContext.CurrentContext.CancellationToken);

        Assert.That(result, Is.Not.Zero);
    }

    [Test]
    public async Task ExecuteAsync_GivenOutputAlreadyExistsWithoutOverwrite_ReturnsError()
    {
        const string input = SimpleTestIdl;

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, input, TestContext.CurrentContext.CancellationToken);

        // copy to ensure it already exists
        File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestProtocol.avpr"));

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
        File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestProtocol.avpr"));

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
        File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestProtocol.avpr"));

        // expect an error in overwriting if in the same dir
        var result = await _app.RunAsync([sourceFile.FullName], TestContext.CurrentContext.CancellationToken);

        // restore dir
        Directory.SetCurrentDirectory(originalDir);

        Assert.That(result, Is.Not.Zero);
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

    private const string ProtocolOneJson = @"{""protocol"":""ProtocolOne"",""types"":[],""messages"":{}}";
    private const string ProtocolTwoJson = @"{""protocol"":""ProtocolTwo"",""types"":[],""messages"":{}}";

    private void SetupTranslatorToParseProtocolFromContent()
    {
        _idlTranslator
            .Setup(t => t.Translate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string content, string _, CancellationToken __) => IdlParseResult.Protocol(AvroProtocol.Parse(content)));
    }

    [Test]
    public async Task ExecuteAsync_GivenDirectoryInput_ProcessesContainedFiles()
    {
        var inputDir = new DirectoryInfo(Path.Combine(_tempDir.DirectoryPath, "inputs"));
        inputDir.Create();
        await File.WriteAllTextAsync(Path.Combine(inputDir.FullName, "input.avdl"), SimpleTestIdl, TestContext.CurrentContext.CancellationToken);

        var outputDir = new DirectoryInfo(Path.Combine(_tempDir.DirectoryPath, "out"));
        outputDir.Create();

        var result = await _app.RunAsync([inputDir.FullName, "--output-dir", outputDir.FullName], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(File.Exists(Path.Combine(outputDir.FullName, "TestProtocol.avpr")), Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenGlobInput_ProcessesMatchingFiles()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "input.avdl"), SimpleTestIdl, TestContext.CurrentContext.CancellationToken);

        var outputDir = new DirectoryInfo(Path.Combine(_tempDir.DirectoryPath, "out"));
        outputDir.Create();

        var glob = Path.Combine(_tempDir.DirectoryPath, "*.avdl");
        var result = await _app.RunAsync([glob, "--output-dir", outputDir.FullName], TestContext.CurrentContext.CancellationToken);

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
        await File.WriteAllTextAsync(one, ProtocolOneJson, TestContext.CurrentContext.CancellationToken);
        await File.WriteAllTextAsync(two, ProtocolTwoJson, TestContext.CurrentContext.CancellationToken);

        var outputDir = new DirectoryInfo(Path.Combine(_tempDir.DirectoryPath, "out"));
        outputDir.Create();

        var result = await _app.RunAsync([one, two, "--output-dir", outputDir.FullName], TestContext.CurrentContext.CancellationToken);

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
        await File.WriteAllTextAsync(one, SimpleTestIdl, TestContext.CurrentContext.CancellationToken);
        await File.WriteAllTextAsync(two, SimpleTestIdl, TestContext.CurrentContext.CancellationToken);

        var outputDir = new DirectoryInfo(Path.Combine(_tempDir.DirectoryPath, "out"));
        outputDir.Create();

        var result = await _app.RunAsync([one, two, "--overwrite", "--output-dir", outputDir.FullName], TestContext.CurrentContext.CancellationToken);

        Assert.That(result.ExitCode, Is.Not.Zero);
    }

    [Test]
    public async Task ExecuteAsync_GivenFailFastAndFailingFirstInput_DoesNotProcessRest()
    {
        _idlTranslator
            .Setup(t => t.Translate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string content, string _, CancellationToken __) =>
                content.Contains("BAD")
                    ? throw new InvalidOperationException("bad input")
                    : IdlParseResult.Protocol(AvroProtocol.Parse(content)));

        // Ordinal ordering within the directory means 1_bad.avdl is processed before 2_good.avdl.
        var bad = Path.Combine(_tempDir.DirectoryPath, "1_bad.avdl");
        var good = Path.Combine(_tempDir.DirectoryPath, "2_good.avdl");
        await File.WriteAllTextAsync(bad, "BAD", TestContext.CurrentContext.CancellationToken);
        await File.WriteAllTextAsync(good, ProtocolOneJson, TestContext.CurrentContext.CancellationToken);

        var outputDir = new DirectoryInfo(Path.Combine(_tempDir.DirectoryPath, "out"));
        outputDir.Create();

        var glob = Path.Combine(_tempDir.DirectoryPath, "*.avdl");
        var result = await _app.RunAsync([glob, "--fail-fast", "--output-dir", outputDir.FullName], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(File.Exists(Path.Combine(outputDir.FullName, "ProtocolOne.avpr")), Is.False);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenUnreadableInputAlongsideValidOne_ReportsItAndCompilesTheOther()
    {
        var console = new TestConsole().Width(200);
        var registrar = new FakeTypeRegistrar();
        registrar.RegisterInstance(typeof(IdlCommand), new IdlCommand(new StatusConsole(console), _streams, _idlTranslator.Object));

        var app = new CommandAppTester(registrar);
        app.SetDefaultCommand<IdlCommand>();

        var good = Path.Combine(_tempDir.DirectoryPath, "good.avdl");
        await File.WriteAllTextAsync(good, SimpleTestIdl, TestContext.CurrentContext.CancellationToken);

        using var unreadable = new UnreadableFile(Path.Combine(_tempDir.DirectoryPath, "locked.avdl"));

        var outputDir = new DirectoryInfo(Path.Combine(_tempDir.DirectoryPath, "out"));
        outputDir.Create();

        var result = await app.RunAsync([unreadable.Path, good, "--output-dir", outputDir.FullName], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(console.Output, Does.Contain($"Unable to read '{unreadable.Path}'"));
            Assert.That(File.Exists(Path.Combine(outputDir.FullName, "TestProtocol.avpr")), Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenFailFastAndUnreadableFirstInput_DoesNotProcessRest()
    {
        var good = Path.Combine(_tempDir.DirectoryPath, "2_good.avdl");
        await File.WriteAllTextAsync(good, SimpleTestIdl, TestContext.CurrentContext.CancellationToken);

        using var unreadable = new UnreadableFile(Path.Combine(_tempDir.DirectoryPath, "1_locked.avdl"));

        var outputDir = new DirectoryInfo(Path.Combine(_tempDir.DirectoryPath, "out"));
        outputDir.Create();

        // Ordinal ordering within the directory means the unreadable file comes first.
        var glob = Path.Combine(_tempDir.DirectoryPath, "*.avdl");
        var result = await _app.RunAsync([glob, "--fail-fast", "--output-dir", outputDir.FullName], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(File.Exists(Path.Combine(outputDir.FullName, "TestProtocol.avpr")), Is.False);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenInputWithUntokenisableCharacter_ReportsPositionAndReturnsError()
    {
        var console = new TestConsole().Width(200);
        var registrar = new FakeTypeRegistrar();
        var translator = new IdlToAvroTranslator(new PhysicalIdlFileReader());
        registrar.RegisterInstance(typeof(IdlCommand), new IdlCommand(new StatusConsole(console), _streams, translator));

        var app = new CommandAppTester(registrar);
        app.SetDefaultCommand<IdlCommand>();

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "lex.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, "protocol TestProtocol { record TestRecord { string a; # } }", TestContext.CurrentContext.CancellationToken);

        var result = await app.RunAsync([sourceFile.FullName, "--output-dir", _tempDir.DirectoryPath], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(console.Output, Does.Contain("Unable to parse IDL document"));
            Assert.That(console.Output, Does.Contain("line 1:54"));
            Assert.That(File.Exists(Path.Combine(_tempDir.DirectoryPath, "TestProtocol.avpr")), Is.False);
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
            Assert.That(File.Exists(Path.Combine(outputDir, "TestProtocol.avpr")), Is.True);
        }
    }
}
