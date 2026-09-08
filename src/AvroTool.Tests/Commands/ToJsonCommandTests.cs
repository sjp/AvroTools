using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avro;
using Avro.File;
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
internal class ToJsonCommandTests
{
    private const string SchemaJson = """{"type":"record","name":"Person","namespace":"ns","fields":[{"name":"name","type":"string"},{"name":"nickname","type":["null","string"],"default":null}]}""";

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
        var command = new ToJsonCommand(_console.Object, _streams);
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

        var result = await _app.RunAsync([avroFile], TestContext.CurrentContext.CancellationToken);

        var lines = _streams.OutputText.Trim().ReplaceLineEndings("\n").Split('\n');
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(lines, Has.Length.EqualTo(2));
            Assert.That(lines[0], Is.EqualTo("""{"name":"Alice","nickname":{"string":"Ally"}}"""));
            Assert.That(lines[1], Is.EqualTo("""{"name":"Bob","nickname":null}"""));
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenStdin_WritesOneJsonLinePerRecord()
    {
        var schema = Schema;
        var alice = new GenericRecord(schema);
        alice.Add("name", "Alice");
        alice.Add("nickname", "Ally");

        _streams.StandardInputBytes = await File.ReadAllBytesAsync(CreateAvroFile(alice), TestContext.CurrentContext.CancellationToken);

        var result = await _app.RunAsync(["--stdin"], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(_streams.OutputText.Trim(), Is.EqualTo("""{"name":"Alice","nickname":{"string":"Ally"}}"""));
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

        var result = await _app.RunAsync([avroFile, "--pretty"], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(_streams.OutputText, Does.Contain("\n"));
            Assert.That(_streams.OutputText, Does.Contain("  \"name\""));
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenManyRecords_WritesEveryRecordInOrder()
    {
        const int recordCount = 5_000;

        var schema = Schema;
        var records = new GenericRecord[recordCount];
        for (var i = 0; i < recordCount; i++)
        {
            var record = new GenericRecord(schema);
            record.Add("name", i.ToString(CultureInfo.InvariantCulture));
            record.Add("nickname", null);
            records[i] = record;
        }

        var avroFile = CreateAvroFile(records);

        var result = await _app.RunAsync([avroFile], TestContext.CurrentContext.CancellationToken);

        var lines = _streams.OutputText.Trim().ReplaceLineEndings("\n").Split('\n');
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(lines, Has.Length.EqualTo(recordCount));
            Assert.That(lines[0], Is.EqualTo("""{"name":"0","nickname":null}"""));
            Assert.That(lines[^1], Is.EqualTo("""{"name":"4999","nickname":null}"""));
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenEmptyFile_WritesNothing()
    {
        var avroFile = CreateAvroFile();

        var result = await _app.RunAsync([avroFile], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(_streams.OutputText.Trim(), Is.Empty);
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
    public async Task ExecuteAsync_GivenUnreadableFile_ReportsItAndReturnsError()
    {
        var console = new TestConsole().Width(200);
        var registrar = new FakeTypeRegistrar();
        registrar.RegisterInstance(typeof(ToJsonCommand), new ToJsonCommand(new StatusConsole(console), _streams));

        var app = new CommandAppTester(registrar);
        app.SetDefaultCommand<ToJsonCommand>();

        using var unreadable = new UnreadableFile(Path.Combine(_tempDir.DirectoryPath, "People.avro"));

        var result = await app.RunAsync([unreadable.Path], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(console.Output, Does.Contain($"Unable to read '{unreadable.Path}'"));
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
        var path = Path.Combine(_tempDir.DirectoryPath, "unsupported-codec.avro");
        AvroDataFileFixtures.WriteContainerHeaderWithCodec(path, Schema, codecName);

        var console = new TestConsole().Width(200);
        var registrar = new FakeTypeRegistrar();
        registrar.RegisterInstance(typeof(ToJsonCommand), new ToJsonCommand(new StatusConsole(console), _streams));

        var app = new CommandAppTester(registrar);
        app.SetDefaultCommand<ToJsonCommand>();

        var result = await app.RunAsync([path], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(console.Output, Does.Contain($"The container uses the '{codecName}' codec; only 'null' and 'deflate' are supported."));
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenFileThatFailsToDecodeMidStream_WritesTheRecordsDecodedBeforeTheFailure()
    {
        var schema = Schema;
        var alice = new GenericRecord(schema);
        alice.Add("name", "Alice");
        alice.Add("nickname", null);
        var bob = new GenericRecord(schema);
        bob.Add("name", "Bob");
        bob.Add("nickname", null);

        var path = Path.Combine(_tempDir.DirectoryPath, "Truncated.avro");
        long endOfFirstBlock;
        using (var writer = DataFileWriter<GenericRecord>.OpenWriter(new GenericDatumWriter<GenericRecord>(schema), path, Codec.CreateCodec(Codec.Type.Null)))
        {
            // A sync marker forces Alice into her own block, so truncating the file after it
            // leaves a complete first record followed by a second block with no data in it.
            writer.Append(alice);
            endOfFirstBlock = writer.Sync();
            writer.Append(bob);
            writer.Flush();
        }

        // Cutting the file off partway through the second block, rather than exactly at its
        // start, leaves a block header promising data that is not there, which is what turns
        // a merely empty tail into an actual decode failure.
        using (var truncate = new FileStream(path, FileMode.Open, FileAccess.Write))
            truncate.SetLength(endOfFirstBlock + ((truncate.Length - endOfFirstBlock) / 2));

        var result = await _app.RunAsync([path], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(_streams.OutputText.Trim(), Is.EqualTo("""{"name":"Alice","nickname":null}"""));
        }
    }
}
