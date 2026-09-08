using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SJP.Avro.Tools.Idl;

namespace SJP.Avro.Tools.Tests.Idl;

[TestFixture]
internal class NamespacelessImportTests
{
    private TemporaryDirectory _tempDir;
    private IdlToAvroTranslator _translator;

    [SetUp]
    public void Setup()
    {
        _tempDir = new TemporaryDirectory();
        _translator = new IdlToAvroTranslator(new PhysicalIdlFileReader());
    }

    [TearDown]
    public void TearDown() => _tempDir.Dispose();

    [Test]
    public async Task Translate_GivenIdlImportOfNamespacelessProtocol_WritesEmptyNamespaceOntoImportedType()
    {
        _tempDir.WriteFile("inner.avdl", "protocol Inner { record InnerRec { string x; } }");
        var main = _tempDir.WriteFile(
            "main.avdl",
            """@namespace("my.ns") protocol Main { import idl "inner.avdl"; record Outer { InnerRec i; } }""");

        var result = await Translate(main);

        Assert.That(result.Json.SelectToken("types[0].name")?.Value<string>(), Is.EqualTo("InnerRec"));
        Assert.That(result.Json.SelectToken("types[0].namespace")?.Value<string>(), Is.EqualTo(""));
    }

    [Test]
    public async Task Translate_GivenIdlImportOfNamespacelessSchemaSyntaxFile_WritesEmptyNamespaceOntoImportedType()
    {
        _tempDir.WriteFile("types.avdl", "record InnerRec { string x; }");
        var main = _tempDir.WriteFile(
            "main.avdl",
            """@namespace("my.ns") protocol Main { import idl "types.avdl"; record Outer { InnerRec i; } }""");

        var result = await Translate(main);

        Assert.That(result.Json.SelectToken("types[0].name")?.Value<string>(), Is.EqualTo("InnerRec"));
        Assert.That(result.Json.SelectToken("types[0].namespace")?.Value<string>(), Is.EqualTo(""));
    }

    [Test]
    public async Task Translate_GivenProtocolImportOfNamespacelessCompiledProtocol_WritesEmptyNamespaceOntoImportedType()
    {
        _tempDir.WriteFile(
            "inner.avpr",
            """
            {
                "protocol": "Inner",
                "types": [
                    { "type": "record", "name": "InnerRec", "fields": [ { "name": "x", "type": "string" } ] }
                ]
            }
            """);
        var main = _tempDir.WriteFile(
            "main.avdl",
            """@namespace("my.ns") protocol Main { import protocol "inner.avpr"; record Outer { InnerRec i; } }""");

        var result = await Translate(main);

        Assert.That(result.Json.SelectToken("types[0].name")?.Value<string>(), Is.EqualTo("InnerRec"));
        Assert.That(result.Json.SelectToken("types[0].namespace")?.Value<string>(), Is.EqualTo(""));
    }

    [Test]
    public async Task Translate_GivenSchemaImportOfNamespacelessAvsc_WritesEmptyNamespaceOntoImportedType()
    {
        _tempDir.WriteFile(
            "inner.avsc",
            """{ "type": "record", "name": "InnerRec", "fields": [ { "name": "x", "type": "string" } ] }""");
        var main = _tempDir.WriteFile(
            "main.avdl",
            """@namespace("my.ns") protocol Main { import schema "inner.avsc"; record Outer { InnerRec i; } }""");

        var result = await Translate(main);

        Assert.That(result.Json.SelectToken("types[0].name")?.Value<string>(), Is.EqualTo("InnerRec"));
        Assert.That(result.Json.SelectToken("types[0].namespace")?.Value<string>(), Is.EqualTo(""));
    }

    private Task<IdlParseResult> Translate(string filePath)
    {
        var content = File.ReadAllText(filePath);
        return _translator.Translate(content, Path.GetDirectoryName(filePath), filePath, TestContext.CurrentContext.CancellationToken);
    }
}
