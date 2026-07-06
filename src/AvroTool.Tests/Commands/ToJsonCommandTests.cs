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
internal class ToJsonCommandTests
{
    private const string SchemaJson = """{"type":"record","name":"Person","namespace":"ns","fields":[{"name":"name","type":"string"},{"name":"nickname","type":["null","string"],"default":null}]}""";

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
        var command = new ToJsonCommand(_console.Object);
        registrar.RegisterInstance(typeof(ToJsonCommand), command);

        _app = new CommandAppTester(registrar);
        _app.SetDefaultCommand<ToJsonCommand>();
    }

    [TearDown]
    public void TearDown()
    {
        _tempDir?.Dispose();
    }

    private RecordSchema Schema => (RecordSchema)Avro.Schema.Parse(SchemaJson);

    private string CreateAvroFile(params GenericRecord[] records)
    {
        var path = Path.Combine(_tempDir.DirectoryPath, "People.avro");
        AvroDataFileFixtures.WriteContainerFile(path, Schema, records);
        return path;
    }

    [Test]
    public async Task ExecuteAsync_GivenMultipleRecords_WritesOneJsonLinePerRecord()
    {
        var schema = Schema;
        var alice = new GenericRecord(schema);
        alice.Add("name", "Alice");
        alice.Add("nickname", "Ally");
        var bob = new GenericRecord(schema);
        bob.Add("name", "Bob");
        bob.Add("nickname", null);

        var avroFile = CreateAvroFile(alice, bob);

        var originalOut = Console.Out;
        var stdout = new StringWriter();
        Console.SetOut(stdout);
        try
        {
            var result = await _app.RunAsync([avroFile], default);

            var lines = stdout.ToString().Trim().ReplaceLineEndings("\n").Split('\n');
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.Zero);
                Assert.That(lines, Has.Length.EqualTo(2));
                Assert.That(lines[0], Is.EqualTo("""{"name":"Alice","nickname":{"string":"Ally"}}"""));
                Assert.That(lines[1], Is.EqualTo("""{"name":"Bob","nickname":null}"""));
            }
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenPretty_WritesIndentedRecords()
    {
        var schema = Schema;
        var alice = new GenericRecord(schema);
        alice.Add("name", "Alice");
        alice.Add("nickname", null);

        var avroFile = CreateAvroFile(alice);

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
                Assert.That(stdout.ToString(), Does.Contain("  \"name\""));
            }
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenEmptyFile_WritesNothing()
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
                Assert.That(stdout.ToString().Trim(), Is.Empty);
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
