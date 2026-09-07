using System.IO;
using System.Threading.Tasks;
using AvroTool.Commands;
using Moq;
using NUnit.Framework;
using SJP.Avro.Tools.Idl;
using Spectre.Console;
using Spectre.Console.Cli.Testing;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;

namespace AvroTool.Tests.Commands;

[TestFixture]
internal class FingerprintCommandTests
{
    private const string SchemaJson = """{"type":"record","name":"Person","namespace":"ns","doc":"a person","fields":[{"name":"Name","type":"string","doc":"x"},{"name":"Age","type":"int","default":0}]}""";

    // Expected fingerprints of the parsing canonical form of the schema above,
    // as produced by Apache.Avro's SchemaNormalization.
    private const string ExpectedCrc64Hex = "b0e15e3c5393d356";
    private const string ExpectedCrc64Long = "6256506293052170672";
    private const string ExpectedSha256Hex = "dfcf26207b59396b32b55e6269a8413e2ba78708cef6d7370a489c84ae151009";
    private const string ExpectedMd5Base64 = "FccbqyxJ/aW/LIZBmt/kLw==";

    private const string ProtocolJson = """{"protocol":"P","types":[{"type":"record","name":"A","fields":[{"name":"x","type":"string"}]},{"type":"enum","name":"Color","symbols":["RED","GREEN"]}],"messages":{}}""";

    private const string InvalidNameSchemaJson = """{"type":"record","name":"weird-name","fields":[]}""";
    private const string MalformedIdl = "protocol TestProtocol { record TestRecord { string a } }";
    private const string IdlWithMissingImport = """protocol TestProtocol { import idl "absent.avdl"; }""";

    private CommandAppTester _app;
    private TemporaryDirectory _tempDir;
    private TestStandardStreams _streams;
    private Mock<IStatusConsole> _console;
    private Mock<IIdlToAvroTranslator> _idlTranslator;

    [SetUp]
    public void Setup()
    {
        _tempDir = new TemporaryDirectory();

        _console = new Mock<IStatusConsole>(MockBehavior.Strict);
        _console.Setup(c => c.Write(It.IsAny<IRenderable>()));

        _idlTranslator = new Mock<IIdlToAvroTranslator>(MockBehavior.Strict);
        _streams = new TestStandardStreams();

        var registrar = new FakeTypeRegistrar();
        var command = new FingerprintCommand(_console.Object, _streams, _idlTranslator.Object);
        registrar.RegisterInstance(typeof(FingerprintCommand), command);

        _app = new CommandAppTester(registrar);
        _app.SetDefaultCommand<FingerprintCommand>();
    }

    [TearDown]
    public void TearDown()
    {
        _tempDir?.Dispose();
    }

    private async Task<(int ExitCode, string Stdout)> RunAsync(params string[] args)
    {
        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "Person.avsc"));
        await File.WriteAllTextAsync(sourceFile.FullName, SchemaJson, TestContext.CurrentContext.CancellationToken);

        string[] fullArgs = [sourceFile.FullName, .. args];
        var result = await _app.RunAsync(fullArgs, TestContext.CurrentContext.CancellationToken);
        return (result.ExitCode, _streams.OutputText.Trim());
    }

    [Test]
    public async Task ExecuteAsync_GivenDefaults_WritesCrc64AvroHex()
    {
        var (exitCode, stdout) = await RunAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(stdout, Is.EqualTo(ExpectedCrc64Hex));
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenLongFormat_WritesCrc64AvroLong()
    {
        var (exitCode, stdout) = await RunAsync("--format", "long");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(stdout, Is.EqualTo(ExpectedCrc64Long));
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenSha256_WritesSha256Hex()
    {
        var (exitCode, stdout) = await RunAsync("-a", "sha-256");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(stdout, Is.EqualTo(ExpectedSha256Hex));
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenUnderscoredAndUpperCasedAlgorithm_WritesSha256Hex()
    {
        var (exitCode, stdout) = await RunAsync("-a", "SHA_256", "-f", "HEX");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(stdout, Is.EqualTo(ExpectedSha256Hex));
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenMd5Base64_WritesMd5Base64()
    {
        var (exitCode, stdout) = await RunAsync("-a", "md5", "-f", "base64");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(stdout, Is.EqualTo(ExpectedMd5Base64));
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenProtocol_WritesLabelledFingerprintPerType()
    {
        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "P.avpr"));
        await File.WriteAllTextAsync(sourceFile.FullName, ProtocolJson, TestContext.CurrentContext.CancellationToken);

        var result = await _app.RunAsync([sourceFile.FullName], TestContext.CurrentContext.CancellationToken);

        var lines = _streams.OutputText.Trim().ReplaceLineEndings("\n").Split('\n');
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(lines, Has.Length.EqualTo(2));
            Assert.That(lines[0], Does.Match("^[0-9a-f]+  A$"));
            Assert.That(lines[1], Does.Match("^[0-9a-f]+  Color$"));
        }
    }

    [Test]
    public async Task Validate_GivenLongFormatWithNonCrcAlgorithm_ReturnsError()
    {
        var result = await _app.RunAsync(["schema.avsc", "-a", "md5", "-f", "long"], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain("'long' format is only valid for the crc-64-avro algorithm"));
        }
    }

    [Test]
    public async Task Validate_GivenUnknownAlgorithm_ReturnsError()
    {
        var result = await _app.RunAsync(["schema.avsc", "-a", "nope"], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain("Unknown algorithm 'nope'"));
        }
    }

    [Test]
    public async Task Validate_WithStandardInputAndPositionalInput_ReturnsError()
    {
        var result = await _app.RunAsync(["--stdin", "a/b/c.avsc"], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain("A schema file may not be given together with --stdin."));
        }
    }

    [Test]
    public async Task Validate_WithMissingInputFile_ReturnsError()
    {
        var result = await _app.RunAsync([string.Empty], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain("A schema file must be provided."));
        }
    }


    /// <summary>
    /// An app wired to the real IDL translator and a console whose output the test can read,
    /// for the cases where the message under test is produced by an actual parser.
    /// </summary>
    private (CommandAppTester App, TestConsole Console) CreateAppWithRealParsers()
    {
        // wide enough that a parser's message is not wrapped, matching the unwrapped width
        // the tool uses when its output is redirected
        var console = new TestConsole().Width(10000);
        var registrar = new FakeTypeRegistrar();
        var translator = new IdlToAvroTranslator(new PhysicalIdlFileReader());
        registrar.RegisterInstance(typeof(FingerprintCommand), new FingerprintCommand(new StatusConsole(console), _streams, translator));

        var app = new CommandAppTester(registrar);
        app.SetDefaultCommand<FingerprintCommand>();

        return (app, console);
    }

    [Test]
    public async Task ExecuteAsync_GivenSchemaWithInvalidName_ReportsTheSchemaParserMessage()
    {
        var (app, console) = CreateAppWithRealParsers();

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "invalid.avsc"));
        await File.WriteAllTextAsync(sourceFile.FullName, InvalidNameSchemaJson, TestContext.CurrentContext.CancellationToken);

        var result = await app.RunAsync([sourceFile.FullName], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(console.Output, Does.Contain("invalid.avsc"));
            Assert.That(console.Output, Does.Contain("could not be parsed as a JSON schema"));
            Assert.That(console.Output, Does.Contain("weird-name"));
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenIdlWithSyntaxError_ReportsThePositionOfTheError()
    {
        var (app, console) = CreateAppWithRealParsers();

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "syntax.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, MalformedIdl, TestContext.CurrentContext.CancellationToken);

        var result = await app.RunAsync([sourceFile.FullName], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(console.Output, Does.Contain("syntax.avdl"));
            Assert.That(console.Output, Does.Contain("could not be parsed as Avro IDL"));
            Assert.That(console.Output, Does.Contain("Syntax error at line 1:53 - mismatched input '}' expecting"));
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenIdlWithMissingImport_ReportsTheMissingPath()
    {
        var (app, console) = CreateAppWithRealParsers();

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "importing.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, IdlWithMissingImport, TestContext.CurrentContext.CancellationToken);

        var result = await app.RunAsync([sourceFile.FullName], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(console.Output, Does.Contain("importing.avdl"));
            Assert.That(console.Output, Does.Contain("could not be parsed as Avro IDL"));
            Assert.That(console.Output, Does.Contain("File not found"));
            Assert.That(console.Output, Does.Contain("absent.avdl"));
        }
    }
}
