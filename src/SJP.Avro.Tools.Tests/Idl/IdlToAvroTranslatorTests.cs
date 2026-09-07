using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Avro;
using FluentAssertions;
using JsonDiffPatchDotNet;
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SJP.Avro.Tools.Idl;

namespace SJP.Avro.Tools.Tests.Idl;

[TestFixture]
internal class IdlToAvroTranslatorTests
{
    private const string BaseInputNamespace = "SJP.Avro.Tools.Tests.Idl.Data.Input";
    private const string BaseOutputNamespace = "SJP.Avro.Tools.Tests.Idl.Data.Output";

    private static readonly IFileProvider InputFileProvider = new EmbeddedFileProvider(Assembly.GetExecutingAssembly(), BaseInputNamespace);
    private static readonly IFileProvider OutputFileProvider = new EmbeddedFileProvider(Assembly.GetExecutingAssembly(), BaseOutputNamespace);

    private IdlToAvroTranslator _translator;

    [SetUp]
    public void Setup()
    {
        _translator = new IdlToAvroTranslator(new FileProviderIdlFileReader(InputFileProvider));
    }

    [TestCaseSource(nameof(IdlInputOutputFilenames))]
    public async Task ParseIdl_GivenValidIdlInput_MatchesExpectedOutput(string idlSampleResourceName, string avroSampleResourceOutput)
    {
        var inputFile = InputFileProvider.GetFileInfo(idlSampleResourceName);
        var outputFile = OutputFileProvider.GetFileInfo(avroSampleResourceOutput);

        await using var outputFileReadStream = outputFile.CreateReadStream();
        using var outputReader = new StreamReader(outputFileReadStream);
        var outputContents = await outputReader.ReadToEndAsync();

        await using var inputFileReadStream = inputFile.CreateReadStream();
        var parseResult = await _translator.Translate(inputFileReadStream);
        var jsonText = parseResult.Match(p => p.ToString(), s => s.ToString());

        var patcher = new JsonDiffPatch();
        var diffResult = patcher.Diff(JObject.Parse(jsonText), JObject.Parse(outputContents));

        Assert.That(diffResult, Is.Null);
    }

    [Test]
    public void Translate_GivenTextThatCannotBeTokenised_ThrowsWithPosition()
    {
        const string idl = "protocol TestProtocol { record TestRecord { string a; # } }";

        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() => _translator.Translate(idl));

        Assert.That(thrown.Message, Does.Contain("Syntax error at line 1:54"));
    }

    [Test]
    public void Translate_GivenStreamThatCannotBeTokenised_ThrowsWithPosition()
    {
        const string idl = "protocol TestProtocol { record TestRecord { string a; # } }";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(idl));

        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() => _translator.Translate(stream));

        Assert.That(thrown.Message, Does.Contain("Syntax error at line 1:54"));
    }

    private static IEnumerable<object[]> IdlInputOutputFilenames()
    {
        var inputNames = EmbeddedResource.GetEmbeddedResourceNames()
            .Where(n => n.EndsWith(".avdl"))
            .Order()
            .ToList();

        var protocolOutputFileNames = inputNames
            .Select(n => n.Replace(".avdl", ".avpr"))
            .ToHashSet();
        var schemaOutputFileNames = inputNames
            .Select(n => n.Replace(".avdl", ".avsc"))
            .ToHashSet();

        var outputNames = EmbeddedResource.GetEmbeddedResourceNames()
            .Where(n => n.Contains(".Output.") && (protocolOutputFileNames.Contains(n.Replace(".Output.", ".Input.")) || schemaOutputFileNames.Contains(n.Replace(".Output.", ".Input."))))
            .Order()
            .ToList();

        return inputNames
            .Zip(outputNames, (a, b) => new object[]
            {
                a.Replace("Idl.Data.Input.", string.Empty),
                b.Replace("Idl.Data.Output.", string.Empty)
            })
            .ToList();
    }
}

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

        var result = await _translator.Translate(idl, Path.Combine(_tempDir.DirectoryPath, "sub"), default);

        Assert.That(TypeNames(result), Is.EqualTo(new[] { "nested.InnerRecord", "Outer" }));
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
        return _translator.Translate(content, Path.GetDirectoryName(filePath), default);
    }

    private static IEnumerable<string> TypeNames(IdlParseResult result) =>
        result.Match(p => p.Types.Select(t => t.Fullname).ToList(), s => [s.Fullname]);
}

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
        var content = await File.ReadAllTextAsync(filePath);
        var result = await _translator.Translate(content, Path.GetDirectoryName(filePath), default);

        return result.Match(p => p, _ => throw new InvalidOperationException("Expected a protocol."));
    }
}

[TestFixture]
internal class StringLiteralEscapeTests
{
    private IdlToAvroTranslator _translator;

    [SetUp]
    public void Setup()
    {
        _translator = new IdlToAvroTranslator(new PhysicalIdlFileReader());
    }

    [TestCase(@"line1\nline2", "line1\nline2")]
    [TestCase(@"a\rb", "a\rb")]
    [TestCase(@"a\tb", "a\tb")]
    [TestCase(@"a\bb", "a\bb")]
    [TestCase(@"a\fb", "a\fb")]
    [TestCase(@"a\\b", @"a\b")]
    [TestCase(@"a\'b", "a'b")]
    [TestCase(@"say \""hi\""", "say \"hi\"")]
    [TestCase(@"Aé", "Aé")]
    [TestCase("café", "café")]
    [TestCase(@"\u0041", "A")]
    [TestCase(@"\u00e9", "é")]
    [TestCase(@"\u00E9", "é")]
    [TestCase(@"\0", "\0")]
    [TestCase(@"\101", "A")]
    [TestCase(@"\7", "\a")]
    [TestCase(@"\377", "ÿ")]
    public async Task Translate_GivenEscapeSequenceInDefault_DecodesIt(string literal, string expected)
    {
        var defaultValue = await GetFieldDefault($@"protocol P {{ record R {{ string s = ""{literal}""; }} }}");

        Assert.That(defaultValue, Is.EqualTo(expected));
    }

    [Test]
    public async Task Translate_GivenEscapeSequenceInDefault_SerialisesItEscapedOnce()
    {
        var protocol = await TranslateProtocol(@"protocol P { record R { string s = ""line1\nline2 \""q\""""; } }");

        Assert.That(protocol.ToString(), Does.Contain(@"""line1\nline2 \""q\"""""));
    }

    [Test]
    public async Task Translate_GivenEscapeSequenceInDocProperty_DecodesIt()
    {
        var protocol = await TranslateProtocol(@"protocol P { @doc(""a\nb"") record R { string s; } }");
        var record = (RecordSchema)protocol.Types.First();

        Assert.That(record.Documentation, Is.EqualTo("a\nb"));
    }

    [Test]
    public async Task Translate_GivenEscapeSequenceInCustomProperty_DecodesIt()
    {
        var protocol = await TranslateProtocol(@"protocol P { @custom(""a\tb"") record R { string s; } }");
        var record = (RecordSchema)protocol.Types.First();

        Assert.That(record.GetProperty("custom"), Is.EqualTo("\"a\\tb\""));
    }

    [Test]
    public async Task Translate_GivenEscapeSequenceInJsonObjectKey_DecodesIt()
    {
        var protocol = await TranslateProtocol(@"protocol P { @custom({ ""a\""b"": 1 }) record R { string s; } }");
        var record = (RecordSchema)protocol.Types.First();
        var custom = JObject.Parse(record.GetProperty("custom"));

        Assert.That(custom.Properties().Select(p => p.Name), Is.EqualTo(new[] { "a\"b" }));
    }

    [Test]
    public async Task Translate_GivenEscapeSequenceInImportLocation_DecodesItBeforeResolving()
    {
        using var tempDir = new TemporaryDirectory();
        tempDir.WriteFile("inner.avdl", @"@namespace(""nested"") protocol Inner { record InnerRecord { string x; } }");
        var main = tempDir.WriteFile("main.avdl", @"protocol Main { import idl ""\u0069nner.avdl""; record Outer { nested.InnerRecord i; } }");

        var content = await File.ReadAllTextAsync(main);
        var result = await _translator.Translate(content, Path.GetDirectoryName(main), default);

        Assert.That(result.Match(p => p.Types.Select(t => t.Fullname).ToList(), s => [s.Fullname]), Does.Contain("nested.InnerRecord"));
    }

    private async Task<string> GetFieldDefault(string idl)
    {
        var protocol = await TranslateProtocol(idl);
        var record = (RecordSchema)protocol.Types.First();

        return record.Fields[0].DefaultValue.Value<string>();
    }

    private async Task<Protocol> TranslateProtocol(string idl)
    {
        var result = await _translator.Translate(idl);

        return result.Match(p => p, _ => throw new InvalidOperationException("Expected a protocol."));
    }
}

[TestFixture]
internal class NumericLiteralTests
{
    private IdlToAvroTranslator _translator;

    [SetUp]
    public void Setup()
    {
        _translator = new IdlToAvroTranslator(new PhysicalIdlFileReader());
    }

    [TestCase("42", 42L)]
    [TestCase("42L", 42L)]
    [TestCase("42l", 42L)]
    [TestCase("-42L", -42L)]
    [TestCase("0", 0L)]
    [TestCase("0x10", 16L)]
    [TestCase("0X10", 16L)]
    [TestCase("-0x10", -16L)]
    [TestCase("0x7fffffffffffffff", long.MaxValue)]
    [TestCase("010", 8L)]
    [TestCase("-010", -8L)]
    [TestCase("0777L", 511L)]
    public async Task Translate_GivenIntegerDefault_ParsesEveryLiteralForm(string literal, long expected)
    {
        var defaultValue = await GetFieldDefault($"protocol P {{ record R {{ long v = {literal}; }} }}");

        Assert.That(defaultValue.Value<long>(), Is.EqualTo(expected));
    }

    [TestCase("1.5", 1.5d)]
    [TestCase("1.5f", 1.5d)]
    [TestCase("1.5F", 1.5d)]
    [TestCase("1.5d", 1.5d)]
    [TestCase("1.5D", 1.5d)]
    [TestCase("+1.0", 1.0d)]
    [TestCase("-1.5", -1.5d)]
    [TestCase(".5", 0.5d)]
    [TestCase("1e3", 1000d)]
    [TestCase("1e3d", 1000d)]
    [TestCase("1E-3", 0.001d)]
    [TestCase("5f", 5d)]
    [TestCase("0x1p3", 8d)]
    [TestCase("-0x1p3", -8d)]
    [TestCase("0x1.8p1", 3d)]
    [TestCase("0x1p-1", 0.5d)]
    [TestCase("0x1p3f", 8d)]
    public async Task Translate_GivenFloatingPointDefault_ParsesEveryLiteralForm(string literal, double expected)
    {
        var defaultValue = await GetFieldDefault($"protocol P {{ record R {{ double v = {literal}; }} }}");

        Assert.That(defaultValue.Value<double>(), Is.EqualTo(expected));
    }

    [TestCase("NaN")]
    [TestCase("Infinity")]
    [TestCase("-Infinity")]
    public void Translate_GivenNonFiniteDefault_ThrowsWithExplanation(string literal)
    {
        var idl = $"protocol P {{ record R {{ double v = {literal}; }} }}";

        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() => _translator.Translate(idl));

        Assert.That(thrown.Message, Does.Contain(literal).And.Contain("no JSON representation"));
    }

    [TestCase("16", 16)]
    [TestCase("0x10", 16)]
    [TestCase("010", 8)]
    [TestCase("16L", 16)]
    public async Task Translate_GivenFixedSize_ParsesEveryLiteralForm(string literal, int expected)
    {
        var protocol = await TranslateProtocol($"protocol P {{ fixed F({literal}); }}");
        var fixedSchema = (FixedSchema)protocol.Types.First();

        Assert.That(fixedSchema.Size, Is.EqualTo(expected));
    }

    [Test]
    public async Task Translate_GivenDecimalPrecisionAndScale_ParsesEveryLiteralForm()
    {
        var protocol = await TranslateProtocol("protocol P { record R { decimal(0x10, 010) v; } }");
        var record = (RecordSchema)protocol.Types.First();
        var schema = record.Fields[0].Schema;

        Assert.Multiple(() =>
        {
            Assert.That(schema.GetProperty("precision"), Is.EqualTo("16"));
            Assert.That(schema.GetProperty("scale"), Is.EqualTo("8"));
        });
    }

    [Test]
    public void Translate_GivenFixedSizeTooLargeForAnInt32_ThrowsWithExplanation()
    {
        const string idl = "protocol P { fixed F(99999999999); }";

        var thrown = Assert.ThrowsAsync<FormatException>(() => _translator.Translate(idl));

        Assert.That(thrown.Message, Does.Contain("32-bit integer"));
    }

    [Test]
    public void Translate_GivenIntegerDefaultTooLargeForAnInt64_ThrowsWithExplanation()
    {
        const string idl = "protocol P { record R { long v = 99999999999999999999; } }";

        var thrown = Assert.ThrowsAsync<FormatException>(() => _translator.Translate(idl));

        Assert.That(thrown.Message, Does.Contain("64-bit integer"));
    }

    private async Task<JToken> GetFieldDefault(string idl)
    {
        var protocol = await TranslateProtocol(idl);
        var record = (RecordSchema)protocol.Types.First();

        return record.Fields[0].DefaultValue;
    }

    private async Task<Protocol> TranslateProtocol(string idl)
    {
        var result = await _translator.Translate(idl);

        return result.Match(p => p, _ => throw new InvalidOperationException("Expected a protocol."));
    }
}
