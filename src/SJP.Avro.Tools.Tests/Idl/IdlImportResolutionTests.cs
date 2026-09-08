using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SJP.Avro.Tools.Idl;

namespace SJP.Avro.Tools.Tests.Idl;

[TestFixture]
internal class IdlImportResolutionTests
{
    private const string DeepIdl = "@namespace(\"nested\") protocol Deep { record DeepRecord { string y; } }";
    private const string InnerIdl = "@namespace(\"nested\") protocol Inner { import idl \"deep/deep.avdl\"; record InnerRecord { nested.DeepRecord d; } }";

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
    public async Task Translate_GivenImportInAnotherDirectory_ResolvesRelativeToImportingFile()
    {
        _tempDir.WriteFile(Path.Combine("sub", "inner.avdl"), "@namespace(\"nested\") protocol Inner { record InnerRecord { string x; } }");
        var main = _tempDir.WriteFile(Path.Combine("sub", "main.avdl"), "protocol Main { import idl \"inner.avdl\"; record Outer { nested.InnerRecord i; } }");

        var result = await Translate(main);

        Assert.That(TypeNames(result), Is.EqualTo(new[] { "nested.InnerRecord", "Outer" }));
    }

    [Test]
    public async Task Translate_GivenNestedImportTwoLevelsDeep_ResolvesEachAgainstItsOwnDirectory()
    {
        _tempDir.WriteFile(Path.Combine("sub", "deep", "deep.avdl"), DeepIdl);
        _tempDir.WriteFile(Path.Combine("sub", "inner.avdl"), InnerIdl);
        var main = _tempDir.WriteFile(Path.Combine("sub", "main.avdl"), "protocol Main { import idl \"inner.avdl\"; record Outer { nested.InnerRecord i; } }");

        var result = await Translate(main);

        Assert.That(TypeNames(result), Is.EqualTo(new[] { "nested.DeepRecord", "nested.InnerRecord", "Outer" }));
    }

    [Test]
    public async Task Translate_GivenSameFileImportedByTwoSpellings_ImportsItOnce()
    {
        _tempDir.WriteFile(Path.Combine("sub", "inner.avdl"), "@namespace(\"nested\") protocol Inner { record InnerRecord { string x; } }");
        var main = _tempDir.WriteFile(
            Path.Combine("sub", "main.avdl"),
            "protocol Main { import idl \"inner.avdl\"; import idl \"./inner.avdl\"; record Outer { nested.InnerRecord i; } }");

        var result = await Translate(main);

        Assert.That(TypeNames(result), Is.EqualTo(new[] { "nested.InnerRecord", "Outer" }));
    }

    [Test]
    public async Task Translate_GivenAbsoluteImportPath_ResolvesIt()
    {
        var inner = _tempDir.WriteFile(Path.Combine("sub", "inner.avdl"), "@namespace(\"nested\") protocol Inner { record InnerRecord { string x; } }");
        var main = _tempDir.WriteFile(
            "main.avdl",
            $"protocol Main {{ import idl \"{inner.Replace("\\", "\\\\")}\"; record Outer {{ nested.InnerRecord i; }} }}");

        var result = await Translate(main);

        Assert.That(TypeNames(result), Is.EqualTo(new[] { "nested.InnerRecord", "Outer" }));
    }

    [Test]
    public async Task Translate_GivenContentReadFromElsewhereWithBaseDirectory_ResolvesAgainstThatDirectory()
    {
        _tempDir.WriteFile(Path.Combine("sub", "inner.avdl"), "@namespace(\"nested\") protocol Inner { record InnerRecord { string x; } }");
        const string idl = "protocol Main { import idl \"inner.avdl\"; record Outer { nested.InnerRecord i; } }";

        var result = await _translator.Translate(idl, Path.Combine(_tempDir.DirectoryPath, "sub"), TestContext.CurrentContext.CancellationToken);

        Assert.That(TypeNames(result), Is.EqualTo(new[] { "nested.InnerRecord", "Outer" }));
    }

    [Test]
    public async Task Translate_GivenImportedMessageReferencingRecordAndError_QualifiesReferencesAgainstSourceNamespace()
    {
        _tempDir.WriteFile(
            Path.Combine("sub", "inner.avdl"),
            """
            @namespace("ns")
            protocol Inner {
              record Rec { int x; }
              error Oops { string msg; }
              Rec get(Rec r) throws Oops;
            }
            """);
        var main = _tempDir.WriteFile(Path.Combine("sub", "main.avdl"), "protocol Outer { import idl \"inner.avdl\"; }");

        var result = await Translate(main);

        Assert.That(result.Json.SelectToken("messages.get.request[0].type")?.Value<string>(), Is.EqualTo("ns.Rec"));
        Assert.That(result.Json.SelectToken("messages.get.response")?.Value<string>(), Is.EqualTo("ns.Rec"));
        Assert.That(result.Json.SelectToken("messages.get.errors[0]")?.Value<string>(), Is.EqualTo("ns.Oops"));
    }

    [Test]
    public async Task Translate_GivenImportedMessageReferencingRecordAndErrorWithImporterInDifferentNamespace_QualifiesReferencesAgainstSourceNamespace()
    {
        _tempDir.WriteFile(
            Path.Combine("sub", "inner.avdl"),
            """
            @namespace("ns")
            protocol Inner {
              record Rec { int x; }
              error Oops { string msg; }
              Rec get(Rec r) throws Oops;
            }
            """);
        var main = _tempDir.WriteFile(
            Path.Combine("sub", "main.avdl"),
            """@namespace("other") protocol Outer { import idl "inner.avdl"; }""");

        var result = await Translate(main);

        Assert.That(result.Json.SelectToken("messages.get.request[0].type")?.Value<string>(), Is.EqualTo("ns.Rec"));
        Assert.That(result.Json.SelectToken("messages.get.response")?.Value<string>(), Is.EqualTo("ns.Rec"));
        Assert.That(result.Json.SelectToken("messages.get.errors[0]")?.Value<string>(), Is.EqualTo("ns.Oops"));
    }

    [Test]
    public async Task Translate_GivenImportedMessageReferencingRecordWithNoSourceNamespace_LeavesReferenceBare()
    {
        _tempDir.WriteFile(
            Path.Combine("sub", "inner.avdl"),
            "protocol Inner { record Rec { int x; } Rec get(Rec r); }");
        var main = _tempDir.WriteFile(
            Path.Combine("sub", "main.avdl"),
            """@namespace("other") protocol Outer { import idl "inner.avdl"; }""");

        var result = await Translate(main);

        Assert.That(result.Json.SelectToken("messages.get.request[0].type")?.Value<string>(), Is.EqualTo("Rec"));
        Assert.That(result.Json.SelectToken("messages.get.response")?.Value<string>(), Is.EqualTo("Rec"));
    }

    [Test]
    public void Translate_GivenImportMissingFromTheImportingFilesDirectory_Throws()
    {
        _tempDir.WriteFile("inner.avdl", "@namespace(\"nested\") protocol Inner { record InnerRecord { string x; } }");
        var main = _tempDir.WriteFile(Path.Combine("sub", "main.avdl"), "protocol Main { import idl \"inner.avdl\"; record Outer { string x; } }");

        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() => Translate(main));

        Assert.That(thrown.Message, Does.Contain("Failed to import IDL"));
    }

    private Task<IdlParseResult> Translate(string filePath)
    {
        var content = File.ReadAllText(filePath);
        return _translator.Translate(content, Path.GetDirectoryName(filePath), TestContext.CurrentContext.CancellationToken);
    }

    private static IEnumerable<string> TypeNames(IdlParseResult result) =>
        result.Match(p => p.Types.Select(t => t.Fullname).ToList(), s => [s.Fullname]);
}
