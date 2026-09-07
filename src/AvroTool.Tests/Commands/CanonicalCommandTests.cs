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
internal class CanonicalCommandTests
{
    private const string SchemaJson = """{"type":"record","name":"Person","namespace":"ns","doc":"a person","fields":[{"name":"Name","type":"string","doc":"x"},{"name":"Age","type":"int","default":0}]}""";
    private const string ExpectedCanonical = """{"name":"ns.Person","type":"record","fields":[{"name":"Name","type":"string"},{"name":"Age","type":"int"}]}""";

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
        var command = new CanonicalCommand(_console.Object, _streams, _idlTranslator.Object);
        registrar.RegisterInstance(typeof(CanonicalCommand), command);

        _app = new CommandAppTester(registrar);
        _app.SetDefaultCommand<CanonicalCommand>();
    }

    [TearDown]
    public void TearDown()
    {
        _tempDir?.Dispose();
    }

    [Test]
    public async Task ExecuteAsync_GivenSchemaFile_WritesCanonicalFormToStdout()
    {
        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "Person.avsc"));
        await File.WriteAllTextAsync(sourceFile.FullName, SchemaJson, TestContext.CurrentContext.CancellationToken);

        var result = await _app.RunAsync([sourceFile.FullName], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(_streams.OutputText.Trim(), Is.EqualTo(ExpectedCanonical));
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenStdin_WritesCanonicalFormToStdout()
    {
        _streams.StandardInputText = SchemaJson;

        var result = await _app.RunAsync(["--stdin"], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(_streams.OutputText.Trim(), Is.EqualTo(ExpectedCanonical));
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenUnreadableFile_ReportsItAndReturnsError()
    {
        var console = new TestConsole().Width(200);
        var registrar = new FakeTypeRegistrar();
        registrar.RegisterInstance(typeof(CanonicalCommand), new CanonicalCommand(new StatusConsole(console), _streams, _idlTranslator.Object));

        var app = new CommandAppTester(registrar);
        app.SetDefaultCommand<CanonicalCommand>();

        using var unreadable = new UnreadableFile(Path.Combine(_tempDir.DirectoryPath, "Person.avsc"));

        var result = await app.RunAsync([unreadable.Path], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(console.Output, Does.Contain($"Unable to read '{unreadable.Path}'"));
            Assert.That(_streams.OutputText, Is.Empty);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenProtocol_WritesOneCanonicalFormPerType()
    {
        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "P.avpr"));
        await File.WriteAllTextAsync(sourceFile.FullName, ProtocolJson, TestContext.CurrentContext.CancellationToken);

        var result = await _app.RunAsync([sourceFile.FullName], TestContext.CurrentContext.CancellationToken);

        var lines = _streams.OutputText.Trim().ReplaceLineEndings("\n").Split('\n');
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(lines, Has.Length.EqualTo(2));
            Assert.That(lines[0], Does.Contain("\"name\":\"A\""));
            Assert.That(lines[1], Does.Contain("\"name\":\"Color\""));
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

    [Test]
    public async Task Validate_WithNonExistentInputFile_ReturnsError()
    {
        const string schemaFile = "a/b/c.avsc";

        var result = await _app.RunAsync([schemaFile], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain($"A schema file could not be found at: {schemaFile}"));
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
        registrar.RegisterInstance(typeof(CanonicalCommand), new CanonicalCommand(new StatusConsole(console), _streams, translator));

        var app = new CommandAppTester(registrar);
        app.SetDefaultCommand<CanonicalCommand>();

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
