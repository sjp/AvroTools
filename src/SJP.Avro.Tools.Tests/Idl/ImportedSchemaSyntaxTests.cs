using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avro;
using NUnit.Framework;
using SJP.Avro.Tools.Idl;

namespace SJP.Avro.Tools.Tests.Idl;

[TestFixture]
internal class ImportedSchemaSyntaxTests
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
    public async Task Translate_GivenImportedSchemaFileWithForwardReference_ResolvesTheReference()
    {
        _tempDir.WriteFile("types.avdl", "namespace x.y; record A { B b; } record B { string s; }");
        var main = _tempDir.WriteFile("main.avdl", """protocol P { import idl "types.avdl"; record C { x.y.A a; } }""");

        var protocol = await TranslateProtocol(main);
        var a = (RecordSchema)protocol.Types.First(t => t.Name == "A");

        Assert.That(a.Fields[0].Schema.Fullname, Is.EqualTo("x.y.B"));
    }

    [Test]
    public async Task Translate_GivenImportedSchemaFileWithForwardReference_ReferencesItFromTheImportingFile()
    {
        _tempDir.WriteFile("types.avdl", "namespace x.y; record A { B b; } record B { string s; }");
        var main = _tempDir.WriteFile("main.avdl", """protocol P { import idl "types.avdl"; record C { x.y.B b; } }""");

        var protocol = await TranslateProtocol(main);
        var c = (RecordSchema)protocol.Types.First(t => t.Name == "C");

        Assert.That(c.Fields[0].Schema.Fullname, Is.EqualTo("x.y.B"));
    }

    [Test]
    public async Task Translate_GivenImportedSchemaFileDeclaredInUseOrder_KeepsBothTypesAtTheTopLevel()
    {
        _tempDir.WriteFile("types.avdl", "namespace x.y; record B { string s; } record A { B b; }");
        var main = _tempDir.WriteFile("main.avdl", """protocol P { import idl "types.avdl"; record C { x.y.A a; } }""");

        var protocol = await TranslateProtocol(main);

        Assert.That(protocol.Types.Select(t => t.Fullname), Is.EqualTo(new[] { "x.y.B", "x.y.A", "C" }));
    }

    [Test]
    public async Task Translate_GivenImportedSchemaFileWithMutuallyRecursiveTypes_ResolvesBothReferences()
    {
        _tempDir.WriteFile("types.avdl", "namespace x.y; record A { B b; } record B { array<A> a; }");
        var main = _tempDir.WriteFile("main.avdl", """protocol P { import idl "types.avdl"; record C { x.y.A a; } }""");

        var protocol = await TranslateProtocol(main);
        var a = (RecordSchema)protocol.Types.First(t => t.Name == "A");
        var b = (RecordSchema)a.Fields[0].Schema;

        Assert.That(((ArraySchema)b.Fields[0].Schema).ItemSchema.Fullname, Is.EqualTo("x.y.A"));
    }

    private async Task<Protocol> TranslateProtocol(string filePath)
    {
        var content = await File.ReadAllTextAsync(filePath, TestContext.CurrentContext.CancellationToken);
        var result = await _translator.Translate(content, Path.GetDirectoryName(filePath), TestContext.CurrentContext.CancellationToken);

        return result.Match(p => p, _ => throw new InvalidOperationException("Expected a protocol."));
    }
}
