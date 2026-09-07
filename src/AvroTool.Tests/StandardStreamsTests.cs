using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AvroTool.Commands;
using Moq;
using NUnit.Framework;
using SJP.Avro.Tools.Idl;
using Spectre.Console.Cli.Testing;
using Spectre.Console.Rendering;

namespace AvroTool.Tests;

[TestFixture]
internal class ConsoleStandardStreamsTests
{
    private const string SchemaJson = """{"type":"record","name":"Person","namespace":"ns","fields":[{"name":"Name","type":"string"}]}""";
    private const string ExpectedCanonical = """{"name":"ns.Person","type":"record","fields":[{"name":"Name","type":"string"}]}""";

    private const string ProtocolIdl = """
protocol TestProtocol {
  record TestRecord {
    string FirstName;
  }
}
""";

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private TemporaryDirectory _tempDir;

    [SetUp]
    public void Setup()
    {
        _tempDir = new TemporaryDirectory();
    }

    [TearDown]
    public void TearDown()
    {
        _tempDir?.Dispose();
    }

    [Test]
    public async Task ReadAllTextAsync_GivenStandardInputWithUtf8ByteOrderMarkedJson_RemovesByteOrderMark()
    {
        using var streams = CreateStreams(Bytes(Utf8Bom, Encoding.UTF8.GetBytes(SchemaJson)), out _);

        var result = await streams.ReadAllTextAsync(useStandardInput: true, path: null, TestContext.CurrentContext.CancellationToken);

        Assert.That(result, Is.EqualTo(SchemaJson));
    }

    [Test]
    public async Task ReadAllTextAsync_GivenStandardInputWithUtf8ByteOrderMarkedIdl_RemovesByteOrderMark()
    {
        using var streams = CreateStreams(Bytes(Utf8Bom, Encoding.UTF8.GetBytes(ProtocolIdl)), out _);

        var result = await streams.ReadAllTextAsync(useStandardInput: true, path: null, TestContext.CurrentContext.CancellationToken);

        Assert.That(result, Is.EqualTo(ProtocolIdl));
    }

    [Test]
    public async Task ReadAllTextAsync_GivenStandardInputWithUtf16ByteOrderMark_DecodesUsingDetectedEncoding()
    {
        var input = Bytes(Encoding.Unicode.GetPreamble(), Encoding.Unicode.GetBytes(SchemaJson));
        using var streams = CreateStreams(input, out _);

        var result = await streams.ReadAllTextAsync(useStandardInput: true, path: null, TestContext.CurrentContext.CancellationToken);

        Assert.That(result, Is.EqualTo(SchemaJson));
    }

    [Test]
    public async Task ReadAllTextAsync_GivenStandardInputWithNonAsciiText_DecodesAsUtf8()
    {
        const string input = "Café 日本語";
        using var streams = CreateStreams(Encoding.UTF8.GetBytes(input), out _);

        var result = await streams.ReadAllTextAsync(useStandardInput: true, path: null, TestContext.CurrentContext.CancellationToken);

        Assert.That(result, Is.EqualTo(input));
    }

    [Test]
    public async Task ReadAllTextAsync_GivenFilePath_ReadsFileContents()
    {
        var sourceFile = Path.Combine(_tempDir.DirectoryPath, "Person.avsc");
        await File.WriteAllTextAsync(sourceFile, SchemaJson, TestContext.CurrentContext.CancellationToken);

        using var streams = CreateStreams([], out _);

        var result = await streams.ReadAllTextAsync(useStandardInput: false, sourceFile, TestContext.CurrentContext.CancellationToken);

        Assert.That(result, Is.EqualTo(SchemaJson));
    }

    [Test]
    public void Output_WhenWrittenTo_EncodesAsUtf8WithoutByteOrderMark()
    {
        const string payload = "Café 日本語";

        var streams = CreateStreams([], out var output);
        using (streams)
            streams.Output.Write(payload);

        Assert.That(output.ToArray(), Is.EqualTo(Encoding.UTF8.GetBytes(payload)));
    }

    [Test]
    public void Output_WhenNotWrittenTo_LeavesStandardOutputUntouched()
    {
        var opened = false;

        var streams = new ConsoleStandardStreams(() => new MemoryStream(), () => { opened = true; return new MemoryStream(); });
        streams.Dispose();

        Assert.That(opened, Is.False);
    }

    [Test]
    public async Task ExecuteAsync_GivenByteOrderMarkedSchemaOnStandardInput_WritesCanonicalFormAsUtf8()
    {
        var console = new Mock<IStatusConsole>(MockBehavior.Strict);
        console.Setup(c => c.Write(It.IsAny<IRenderable>()));

        var translator = new Mock<IIdlToAvroTranslator>(MockBehavior.Strict);

        var streams = CreateStreams(Bytes(Utf8Bom, Encoding.UTF8.GetBytes(SchemaJson)), out var output);

        var registrar = new FakeTypeRegistrar();
        registrar.RegisterInstance(typeof(CanonicalCommand), new CanonicalCommand(console.Object, streams, translator.Object));

        var app = new CommandAppTester(registrar);
        app.SetDefaultCommand<CanonicalCommand>();

        int exitCode;
        using (streams)
            exitCode = (await app.RunAsync(["--stdin"], TestContext.CurrentContext.CancellationToken)).ExitCode;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(Encoding.UTF8.GetString(output.ToArray()).Trim(), Is.EqualTo(ExpectedCanonical));
        }
    }

    private static ConsoleStandardStreams CreateStreams(byte[] standardInput, out MemoryStream standardOutput)
    {
        var output = new MemoryStream();
        standardOutput = output;
        return new ConsoleStandardStreams(() => new MemoryStream(standardInput, writable: false), () => output);
    }

    private static byte[] Bytes(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        var result = new byte[first.Length + second.Length];
        first.CopyTo(result);
        second.CopyTo(result.AsSpan(first.Length));
        return result;
    }
}
