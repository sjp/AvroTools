using System;
using System.IO;
using System.Threading;
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
        var command = new FingerprintCommand(_console.Object, _idlTranslator.Object);
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
        await File.WriteAllTextAsync(sourceFile.FullName, SchemaJson);

        var originalOut = Console.Out;
        var stdout = new StringWriter();
        Console.SetOut(stdout);
        try
        {
            string[] fullArgs = [sourceFile.FullName, .. args];
            var result = await _app.RunAsync(fullArgs, default);
            return (result.ExitCode, stdout.ToString().Trim());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
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
        await File.WriteAllTextAsync(sourceFile.FullName, ProtocolJson);

        var originalOut = Console.Out;
        var stdout = new StringWriter();
        Console.SetOut(stdout);
        try
        {
            var result = await _app.RunAsync([sourceFile.FullName], default);

            var lines = stdout.ToString().Trim().ReplaceLineEndings("\n").Split('\n');
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.Zero);
                Assert.That(lines, Has.Length.EqualTo(2));
                Assert.That(lines[0], Does.Match("^[0-9a-f]+  A$"));
                Assert.That(lines[1], Does.Match("^[0-9a-f]+  Color$"));
            }
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Test]
    public async Task Validate_GivenLongFormatWithNonCrcAlgorithm_ReturnsError()
    {
        var result = await _app.RunAsync(["schema.avsc", "-a", "md5", "-f", "long"], default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain("'long' format is only valid for the crc-64-avro algorithm"));
        }
    }

    [Test]
    public async Task Validate_GivenUnknownAlgorithm_ReturnsError()
    {
        var result = await _app.RunAsync(["schema.avsc", "-a", "nope"], default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain("Unknown algorithm 'nope'"));
        }
    }

    [Test]
    public async Task Validate_WithMissingInputFile_ReturnsError()
    {
        var result = await _app.RunAsync([string.Empty], default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain("A schema file must be provided."));
        }
    }
}
