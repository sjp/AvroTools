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
internal class CanonicalCommandTests
{
    private const string SchemaJson = """{"type":"record","name":"Person","namespace":"ns","doc":"a person","fields":[{"name":"Name","type":"string","doc":"x"},{"name":"Age","type":"int","default":0}]}""";
    private const string ExpectedCanonical = """{"name":"ns.Person","type":"record","fields":[{"name":"Name","type":"string"},{"name":"Age","type":"int"}]}""";

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
        var command = new CanonicalCommand(_console.Object, _idlTranslator.Object);
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
        await File.WriteAllTextAsync(sourceFile.FullName, SchemaJson);

        var originalOut = Console.Out;
        var stdout = new StringWriter();
        Console.SetOut(stdout);
        try
        {
            var result = await _app.RunAsync([sourceFile.FullName], default);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.Zero);
                Assert.That(stdout.ToString().Trim(), Is.EqualTo(ExpectedCanonical));
            }
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenStdin_WritesCanonicalFormToStdout()
    {
        var originalIn = Console.In;
        var originalOut = Console.Out;
        Console.SetIn(new StringReader(SchemaJson));
        var stdout = new StringWriter();
        Console.SetOut(stdout);
        try
        {
            var result = await _app.RunAsync(["--stdin"], default);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.Zero);
                Assert.That(stdout.ToString().Trim(), Is.EqualTo(ExpectedCanonical));
            }
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenProtocol_WritesOneCanonicalFormPerType()
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
                Assert.That(lines[0], Does.Contain("\"name\":\"A\""));
                Assert.That(lines[1], Does.Contain("\"name\":\"Color\""));
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
            Assert.That(result.Output, Does.Contain("A schema file must be provided."));
        }
    }

    [Test]
    public async Task Validate_WithNonExistentInputFile_ReturnsError()
    {
        const string schemaFile = "a/b/c.avsc";

        var result = await _app.RunAsync([schemaFile], default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain($"A schema file could not be found at: {schemaFile}"));
        }
    }
}
