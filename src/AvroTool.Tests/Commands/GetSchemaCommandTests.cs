using System.IO;
using System.Threading.Tasks;
using Avro;
using Avro.Generic;
using AvroTool.Commands;
using Moq;
using NUnit.Framework;
using Spectre.Console;
using Spectre.Console.Cli.Testing;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;

namespace AvroTool.Tests.Commands;

[TestFixture]
internal class GetSchemaCommandTests
{
    private const string SchemaJson = """{"type":"record","name":"Person","namespace":"ns","fields":[{"name":"name","type":"string"},{"name":"age","type":"int"}]}""";

    private CommandAppTester _app;
    private TemporaryDirectory _tempDir;
    private TestStandardStreams _streams;
    private Mock<IStatusConsole> _console;

    [SetUp]
    public void Setup()
    {
        _tempDir = new TemporaryDirectory();

        _console = new Mock<IStatusConsole>(MockBehavior.Strict);
        _console.Setup(c => c.Write(It.IsAny<IRenderable>()));
        _streams = new TestStandardStreams();

        var registrar = new FakeTypeRegistrar();
        var command = new GetSchemaCommand(_console.Object, _streams);
        registrar.RegisterInstance(typeof(GetSchemaCommand), command);

        _app = new CommandAppTester(registrar);
        _app.SetDefaultCommand<GetSchemaCommand>();
    }

    [TearDown]
    public void TearDown()
    {
        _tempDir?.Dispose();
    }

    private string CreateAvroFile()
    {
        var schema = (RecordSchema)Schema.Parse(SchemaJson);
        var record = new GenericRecord(schema);
        record.Add("name", "Alice");
        record.Add("age", 30);

        var path = Path.Combine(_tempDir.DirectoryPath, "Person.avro");
        AvroDataFileFixtures.WriteContainerFile(path, schema, record);
        return path;
    }

    [Test]
    public async Task ExecuteAsync_GivenAvroFile_WritesWriterSchemaToStdout()
    {
        var avroFile = CreateAvroFile();

        var result = await _app.RunAsync([avroFile], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(_streams.OutputText.Trim(), Is.EqualTo(SchemaJson));
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenStdin_WritesWriterSchemaToStdout()
    {
        _streams.StandardInputBytes = await File.ReadAllBytesAsync(CreateAvroFile(), TestContext.CurrentContext.CancellationToken);

        var result = await _app.RunAsync(["--stdin"], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(_streams.OutputText.Trim(), Is.EqualTo(SchemaJson));
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenPretty_WritesIndentedSchemaToStdout()
    {
        var avroFile = CreateAvroFile();

        var result = await _app.RunAsync([avroFile, "--pretty"], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(_streams.OutputText, Does.Contain("\n"));
            Assert.That(_streams.OutputText, Does.Contain("  "));
        }
    }

    [Test]
    public async Task Validate_WithStandardInputAndPositionalInput_ReturnsError()
    {
        var result = await _app.RunAsync(["--stdin", "a/b/c.avro"], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain("An Avro object container file may not be given together with --stdin."));
        }
    }

    [Test]
    public async Task Validate_WithMissingInputFile_ReturnsError()
    {
        var result = await _app.RunAsync([string.Empty], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain("An Avro object container file must be provided."));
        }
    }

    [Test]
    public async Task Validate_WithNonExistentInputFile_ReturnsError()
    {
        const string avroFile = "a/b/c.avro";

        var result = await _app.RunAsync([avroFile], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain($"An Avro object container file could not be found at: {avroFile}"));
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenNonAvroFile_ReturnsError()
    {
        var path = Path.Combine(_tempDir.DirectoryPath, "not-avro.avro");
        await File.WriteAllTextAsync(path, "not an avro file", TestContext.CurrentContext.CancellationToken);

        var result = await _app.RunAsync([path], TestContext.CurrentContext.CancellationToken);

        Assert.That(result.ExitCode, Is.Not.Zero);
    }

    [TestCase("snappy")]
    [TestCase("bzip2")]
    [TestCase("zstandard")]
    [TestCase("xz")]
    [TestCase("not-a-real-codec")]
    public async Task ExecuteAsync_GivenUnsupportedCodec_NamesTheCodecInTheError(string codecName)
    {
        var schema = (RecordSchema)Schema.Parse(SchemaJson);
        var path = Path.Combine(_tempDir.DirectoryPath, "unsupported-codec.avro");
        AvroDataFileFixtures.WriteContainerHeaderWithCodec(path, schema, codecName);

        var console = new TestConsole().Width(200);
        var registrar = new FakeTypeRegistrar();
        registrar.RegisterInstance(typeof(GetSchemaCommand), new GetSchemaCommand(new StatusConsole(console), _streams));

        var app = new CommandAppTester(registrar);
        app.SetDefaultCommand<GetSchemaCommand>();

        var result = await app.RunAsync([path], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(console.Output, Does.Contain($"The container uses the '{codecName}' codec; only 'null' and 'deflate' are supported."));
        }
    }
}
