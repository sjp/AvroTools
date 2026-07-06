using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AvroTool.Commands;
using Moq;
using NUnit.Framework;
using SJP.Avro.Tools.Idl;
using Spectre.Console;
using Spectre.Console.Cli.Testing;
using Spectre.Console.Rendering;

namespace AvroTool.Tests.Commands;

[TestFixture]
internal class DiffCommandTests
{
    private const string V1 = """{"type":"record","name":"User","namespace":"ex","fields":[{"name":"id","type":"int"},{"name":"name","type":"string"}]}""";
    private const string V2 = """{"type":"record","name":"User","namespace":"ex","fields":[{"name":"id","type":"int"},{"name":"name","type":"string"},{"name":"email","type":"string","default":"none"}]}""";

    private const string V1WithDoc = """{"type":"record","name":"User","namespace":"ex","doc":"old","fields":[{"name":"id","type":"int"},{"name":"name","type":"string"}]}""";
    private const string V2WithDoc = """{"type":"record","name":"User","namespace":"ex","doc":"new","fields":[{"name":"id","type":"int"},{"name":"name","type":"string"}]}""";

    private const string ProtocolJson = """{"protocol":"P","types":[{"type":"record","name":"A","fields":[{"name":"x","type":"string"}]},{"type":"enum","name":"Color","symbols":["RED","GREEN"]}],"messages":{}}""";

    private CommandAppTester _app;
    private TemporaryDirectory _tempDir;
    private Mock<IAnsiConsole> _console;
    private Mock<IIdlToAvroTranslator> _idlTranslator;

    [SetUp]
    public void Setup()
    {
        _tempDir = new TemporaryDirectory();

        _console = new Mock<IAnsiConsole>(MockBehavior.Strict);
        _console.Setup(c => c.Write(It.IsAny<IRenderable>()));

        _idlTranslator = new Mock<IIdlToAvroTranslator>(MockBehavior.Strict);

        var registrar = new FakeTypeRegistrar();
        var command = new DiffCommand(_console.Object, _idlTranslator.Object);
        registrar.RegisterInstance(typeof(DiffCommand), command);

        _app = new CommandAppTester(registrar);
        _app.SetDefaultCommand<DiffCommand>();
    }

    [TearDown]
    public void TearDown()
    {
        _tempDir?.Dispose();
    }

    private string WriteSchema(string name, string content)
    {
        var path = Path.Combine(_tempDir.DirectoryPath, name);
        File.WriteAllText(path, content);
        return path;
    }

    private async Task<(int ExitCode, string Stdout)> RunAsync(params string[] args)
    {
        var originalOut = System.Console.Out;
        var stdout = new StringWriter();
        System.Console.SetOut(stdout);
        try
        {
            var result = await _app.RunAsync(args, default);
            return (result.ExitCode, stdout.ToString().Trim());
        }
        finally
        {
            System.Console.SetOut(originalOut);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenIdenticalSchemas_ReturnsSuccess()
    {
        var a = WriteSchema("a.avsc", V1);
        var b = WriteSchema("b.avsc", V1);

        var (exitCode, _) = await RunAsync(a, b);

        Assert.That(exitCode, Is.Zero);
    }

    [Test]
    public async Task ExecuteAsync_GivenChangedSchemas_ReturnsError()
    {
        var a = WriteSchema("a.avsc", V1);
        var b = WriteSchema("b.avsc", V2);

        var (exitCode, _) = await RunAsync(a, b);

        Assert.That(exitCode, Is.Not.Zero);
    }

    [Test]
    public async Task ExecuteAsync_GivenJsonFlag_WritesMachineReadableResult()
    {
        var a = WriteSchema("a.avsc", V1);
        var b = WriteSchema("b.avsc", V2);

        var (exitCode, stdout) = await RunAsync("--json", a, b);

        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;
        var change = root.GetProperty("changes")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Not.Zero);
            Assert.That(root.GetProperty("identical").GetBoolean(), Is.False);
            Assert.That(change.GetProperty("kind").GetString(), Is.EqualTo("FIELD_ADDED"));
            Assert.That(change.GetProperty("location").GetString(), Is.EqualTo("/fields/email"));
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenJsonFlagAndIdenticalSchemas_ReportsIdentical()
    {
        var a = WriteSchema("a.avsc", V1);
        var b = WriteSchema("b.avsc", V1);

        var (exitCode, stdout) = await RunAsync("--json", a, b);

        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(root.GetProperty("identical").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("changes").GetArrayLength(), Is.Zero);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenVerboseFlag_IncludesMetadataChanges()
    {
        var a = WriteSchema("a.avsc", V1WithDoc);
        var b = WriteSchema("b.avsc", V2WithDoc);

        var (exitCode, stdout) = await RunAsync("--json", "--verbose", a, b);

        using var document = JsonDocument.Parse(stdout);
        var change = document.RootElement.GetProperty("changes")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Not.Zero);
            Assert.That(change.GetProperty("kind").GetString(), Is.EqualTo("METADATA_CHANGED"));
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenNoVerboseFlag_OmitsMetadataChanges()
    {
        var a = WriteSchema("a.avsc", V1WithDoc);
        var b = WriteSchema("b.avsc", V2WithDoc);

        var (exitCode, _) = await RunAsync(a, b);

        Assert.That(exitCode, Is.Zero);
    }

    [Test]
    public async Task ExecuteAsync_GivenMultiTypeProtocolInputForSchemaA_ReturnsError()
    {
        var protocol = WriteSchema("p.avpr", ProtocolJson);
        var b = WriteSchema("b.avsc", V1);

        var (exitCode, _) = await RunAsync(protocol, b);

        Assert.That(exitCode, Is.Not.Zero);
    }

    [Test]
    public async Task ExecuteAsync_GivenMultiTypeProtocolInputForSchemaB_ReturnsError()
    {
        var a = WriteSchema("a.avsc", V1);
        var protocol = WriteSchema("p.avpr", ProtocolJson);

        var (exitCode, _) = await RunAsync(a, protocol);

        Assert.That(exitCode, Is.Not.Zero);
    }

    [Test]
    public async Task ExecuteAsync_GivenUnparsableInput_ReturnsError()
    {
        var a = WriteSchema("a.txt", "not a schema at all");
        var b = WriteSchema("b.avsc", V1);

        var (exitCode, _) = await RunAsync(a, b);

        Assert.That(exitCode, Is.Not.Zero);
    }

    [Test]
    public async Task Validate_GivenMissingFileForSchemaA_ReturnsError()
    {
        var b = WriteSchema("b.avsc", V1);

        var result = await _app.RunAsync(["does/not/exist.avsc", b], default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain("A schema file could not be found at: does/not/exist.avsc"));
        }
    }

    [Test]
    public async Task Validate_GivenMissingFileForSchemaB_ReturnsError()
    {
        var a = WriteSchema("a.avsc", V1);

        var result = await _app.RunAsync([a, "does/not/exist.avsc"], default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain("A schema file could not be found at: does/not/exist.avsc"));
        }
    }
}
