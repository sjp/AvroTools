using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avro;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SJP.Avro.Tools.Idl;

namespace SJP.Avro.Tools.Tests.Idl;

[TestFixture]
internal class ImportedProtocolNamespaceTests
{
    private const string InnerProtocol = """
        {
            "protocol": "Inner",
            "namespace": "other.ns",
            "types": [
                { "type": "record", "name": "InnerRec", "fields": [ { "name": "x", "type": "string" } ] }
            ]
        }
        """;

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
    public async Task Translate_GivenImportedProtocolWithDifferentNamespace_KeepsImportedTypeNamespace()
    {
        _tempDir.WriteFile("inner.avpr", InnerProtocol);
        var main = _tempDir.WriteFile(
            "main.avdl",
            """@namespace("my.ns") protocol Main { import protocol "inner.avpr"; record Outer { other.ns.InnerRec i; } }""");

        var protocol = await TranslateProtocol(main);

        Assert.That(protocol.Types.Select(t => t.Fullname), Is.EqualTo(new[] { "other.ns.InnerRec", "my.ns.Outer" }));
    }

    [Test]
    public async Task Translate_GivenImportedProtocolWithDifferentNamespace_WritesNamespaceOntoImportedType()
    {
        _tempDir.WriteFile("inner.avpr", InnerProtocol);
        var main = _tempDir.WriteFile(
            "main.avdl",
            """@namespace("my.ns") protocol Main { import protocol "inner.avpr"; record Outer { other.ns.InnerRec i; } }""");

        var protocol = await TranslateProtocol(main);
        var protocolJson = JObject.Parse(protocol.ToString());
        var importedType = protocolJson["types"]!.First(t => t["name"]!.ToString() == "InnerRec");

        Assert.That(importedType["namespace"]!.ToString(), Is.EqualTo("other.ns"));
    }

    [Test]
    public void Translate_GivenImportedProtocolTypeReferencedByBareName_Throws()
    {
        _tempDir.WriteFile("inner.avpr", InnerProtocol);
        var main = _tempDir.WriteFile(
            "main.avdl",
            """@namespace("my.ns") protocol Main { import protocol "inner.avpr"; record Outer { InnerRec i; } }""");

        var thrown = Assert.ThrowsAsync<SchemaParseException>(() => TranslateProtocol(main));

        Assert.That(thrown.Message, Does.Contain("InnerRec"));
    }

    [Test]
    public async Task Translate_GivenImportedProtocolWithInlineNestedType_GivesNestedTypeTheImportedNamespace()
    {
        _tempDir.WriteFile(
            "inner.avpr",
            """
            {
                "protocol": "Inner",
                "namespace": "other.ns",
                "types": [
                    {
                        "type": "record",
                        "name": "InnerRec",
                        "fields": [
                            {
                                "name": "nested",
                                "type": {
                                    "type": "array",
                                    "items": { "type": "enum", "name": "NestedEnum", "symbols": [ "A", "B" ] }
                                }
                            }
                        ]
                    }
                ]
            }
            """);
        var main = _tempDir.WriteFile(
            "main.avdl",
            """@namespace("my.ns") protocol Main { import protocol "inner.avpr"; record Outer { other.ns.NestedEnum e; } }""");

        var protocol = await TranslateProtocol(main);
        var outer = (RecordSchema)protocol.Types.First(t => t.Name == "Outer");

        Assert.That(outer.Fields[0].Schema.Fullname, Is.EqualTo("other.ns.NestedEnum"));
    }

    [Test]
    public async Task Translate_GivenImportedProtocolTypeWithItsOwnNamespace_KeepsThatNamespace()
    {
        _tempDir.WriteFile(
            "inner.avpr",
            """
            {
                "protocol": "Inner",
                "namespace": "other.ns",
                "types": [
                    {
                        "type": "record",
                        "name": "InnerRec",
                        "namespace": "third.ns",
                        "fields": [ { "name": "x", "type": "string" } ]
                    }
                ]
            }
            """);
        var main = _tempDir.WriteFile(
            "main.avdl",
            """@namespace("my.ns") protocol Main { import protocol "inner.avpr"; record Outer { third.ns.InnerRec i; } }""");

        var protocol = await TranslateProtocol(main);

        Assert.That(protocol.Types.Select(t => t.Fullname), Does.Contain("third.ns.InnerRec"));
    }

    [Test]
    public async Task Translate_GivenImportedProtocolNamespacedByItsName_KeepsImportedTypeNamespace()
    {
        _tempDir.WriteFile(
            "inner.avpr",
            """
            {
                "protocol": "other.ns.Inner",
                "types": [
                    { "type": "record", "name": "InnerRec", "fields": [ { "name": "x", "type": "string" } ] }
                ]
            }
            """);
        var main = _tempDir.WriteFile(
            "main.avdl",
            """@namespace("my.ns") protocol Main { import protocol "inner.avpr"; record Outer { other.ns.InnerRec i; } }""");

        var protocol = await TranslateProtocol(main);

        Assert.That(protocol.Types.Select(t => t.Fullname), Does.Contain("other.ns.InnerRec"));
    }

    private async Task<Protocol> TranslateProtocol(string filePath)
    {
        var content = await File.ReadAllTextAsync(filePath);
        var result = await _translator.Translate(content, Path.GetDirectoryName(filePath), default);

        return result.Match(p => p, _ => throw new InvalidOperationException("Expected a protocol."));
    }
}
