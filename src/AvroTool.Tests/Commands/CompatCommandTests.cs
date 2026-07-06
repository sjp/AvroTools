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
internal class CompatCommandTests
{
    // v1 -> v2 adds 'email' with a default, so v2 (reader) can read v1 (writer): backward compatible.
    private const string V1 = """{"type":"record","name":"User","namespace":"ex","fields":[{"name":"id","type":"int"},{"name":"name","type":"string"}]}""";
    private const string V2 = """{"type":"record","name":"User","namespace":"ex","fields":[{"name":"id","type":"int"},{"name":"name","type":"string"},{"name":"email","type":"string","default":"none"}]}""";

    // v3 adds 'phone' without a default, breaking backward compatibility against v1.
    private const string V3 = """{"type":"record","name":"User","namespace":"ex","fields":[{"name":"id","type":"int"},{"name":"name","type":"string"},{"name":"phone","type":"string"}]}""";

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
        var command = new CompatCommand(_console.Object, _idlTranslator.Object);
        registrar.RegisterInstance(typeof(CompatCommand), command);

        _app = new CommandAppTester(registrar);
        _app.SetDefaultCommand<CompatCommand>();
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
    public async Task ExecuteAsync_GivenBackwardCompatibleSchemas_ReturnsSuccess()
    {
        var reader = WriteSchema("v2.avsc", V2);
        var writer = WriteSchema("v1.avsc", V1);

        var (exitCode, _) = await RunAsync(reader, writer);

        Assert.That(exitCode, Is.Zero);
    }

    [Test]
    public async Task ExecuteAsync_GivenBackwardIncompatibleSchemas_ReturnsError()
    {
        var reader = WriteSchema("v3.avsc", V3);
        var writer = WriteSchema("v1.avsc", V1);

        var (exitCode, _) = await RunAsync(reader, writer);

        Assert.That(exitCode, Is.Not.Zero);
    }

    [Test]
    public async Task ExecuteAsync_GivenForwardMode_ChecksOppositeDirection()
    {
        // v1 (reader) can read v2 (writer): the extra 'email' field is simply ignored, so forward-compatible.
        var newSchema = WriteSchema("v2.avsc", V2);
        var oldSchema = WriteSchema("v1.avsc", V1);

        var (exitCode, _) = await RunAsync("--mode", "forward", newSchema, oldSchema);

        Assert.That(exitCode, Is.Zero);
    }

    [Test]
    public async Task ExecuteAsync_GivenJsonFlag_WritesMachineReadableResult()
    {
        var reader = WriteSchema("v3.avsc", V3);
        var writer = WriteSchema("v1.avsc", V1);

        var (exitCode, stdout) = await RunAsync("--json", reader, writer);

        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;
        var incompatibility = root.GetProperty("checks")[0].GetProperty("incompatibilities")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Not.Zero);
            Assert.That(root.GetProperty("mode").GetString(), Is.EqualTo("backward"));
            Assert.That(root.GetProperty("compatible").GetBoolean(), Is.False);
            Assert.That(incompatibility.GetProperty("type").GetString(), Is.EqualTo("READER_FIELD_MISSING_DEFAULT_VALUE"));
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenBackwardTransitive_ChecksCandidateAgainstEveryPriorVersion()
    {
        var candidate = WriteSchema("v3.avsc", V3);
        var priorGood = WriteSchema("v2.avsc", V2);
        var priorBad = WriteSchema("v1.avsc", V1);

        // Candidate reader v3 adds 'phone' with no default, so it cannot read any earlier writer that
        // never wrote that field; the transitive chain must fail even though only some priors break.
        var (exitCode, _) = await RunAsync("--mode", "backward-transitive", candidate, priorGood, priorBad);

        Assert.That(exitCode, Is.Not.Zero);
    }

    [Test]
    public async Task Validate_GivenUnknownMode_ReturnsError()
    {
        var reader = WriteSchema("v2.avsc", V2);
        var writer = WriteSchema("v1.avsc", V1);

        var result = await _app.RunAsync(["--mode", "sideways", reader, writer], default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain("Unknown mode 'sideways'"));
        }
    }

    [Test]
    public async Task Validate_GivenSingleSchema_ReturnsError()
    {
        var reader = WriteSchema("v2.avsc", V2);

        var result = await _app.RunAsync([reader], default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain("At least two schema files must be provided."));
        }
    }

    [Test]
    public async Task Validate_GivenNonTransitiveModeWithMoreThanTwoSchemas_ReturnsError()
    {
        var a = WriteSchema("v1.avsc", V1);
        var b = WriteSchema("v2.avsc", V2);
        var c = WriteSchema("v3.avsc", V3);

        var result = await _app.RunAsync([a, b, c], default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain("compares exactly two schemas"));
        }
    }

    [Test]
    public async Task Validate_GivenMissingFile_ReturnsError()
    {
        var reader = WriteSchema("v2.avsc", V2);

        var result = await _app.RunAsync([reader, "does/not/exist.avsc"], default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain("A schema file could not be found at: does/not/exist.avsc"));
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenMultiTypeProtocolInput_ReturnsError()
    {
        var protocol = WriteSchema("p.avpr", ProtocolJson);
        var writer = WriteSchema("v1.avsc", V1);

        var (exitCode, _) = await RunAsync(protocol, writer);

        Assert.That(exitCode, Is.Not.Zero);
    }
}
