using System;
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

namespace AvroTool.Tests.Commands;

[TestFixture]
internal class GetSchemaCommandTests
{
    private const string SchemaJson = """{"type":"record","name":"Person","namespace":"ns","fields":[{"name":"name","type":"string"},{"name":"age","type":"int"}]}""";

    private CommandAppTester _app;
    private TemporaryDirectory _tempDir;
    private Mock<IAnsiConsole> _console;

    [SetUp]
    public void Setup()
    {
        _tempDir = new TemporaryDirectory();

        _console = new Mock<IAnsiConsole>(MockBehavior.Strict);
        _console.Setup(c => c.Write(It.IsAny<IRenderable>()));

        var registrar = new FakeTypeRegistrar();
        var command = new GetSchemaCommand(_console.Object);
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

        var originalOut = Console.Out;
        var stdout = new StringWriter();
        Console.SetOut(stdout);
        try
        {
            var result = await _app.RunAsync([avroFile], default);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.Zero);
                Assert.That(stdout.ToString().Trim(), Is.EqualTo(SchemaJson));
            }
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenPretty_WritesIndentedSchemaToStdout()
    {
        var avroFile = CreateAvroFile();

        var originalOut = Console.Out;
        var stdout = new StringWriter();
        Console.SetOut(stdout);
        try
        {
            var result = await _app.RunAsync([avroFile, "--pretty"], default);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.Zero);
                Assert.That(stdout.ToString(), Does.Contain("\n"));
                Assert.That(stdout.ToString(), Does.Contain("  "));
            }
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Test]
    public async Task Validate_WithMissingInputFile_ReturnsError()
    {
        var result = await _app.RunAsync([string.Empty], default);

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

        var result = await _app.RunAsync([avroFile], default);

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
        await File.WriteAllTextAsync(path, "not an avro file");

        var result = await _app.RunAsync([path], default);

        Assert.That(result.ExitCode, Is.Not.Zero);
    }
}
