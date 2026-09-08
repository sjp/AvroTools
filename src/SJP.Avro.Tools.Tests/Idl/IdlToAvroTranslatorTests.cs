using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
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

    private const string InputResourcePrefix = "Idl.Data.Input.";
    private const string OutputResourcePrefix = "Idl.Data.Output.";

    private static readonly string[] OutputExtensions = [".avpr", ".avsc"];

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

        Assert.That(outputFile.Exists, Is.True, $"No expected output was found for the input '{idlSampleResourceName}', looked for '{avroSampleResourceOutput}'.");

        await using var outputFileReadStream = outputFile.CreateReadStream();
        using var outputReader = new StreamReader(outputFileReadStream);
        var outputContents = await outputReader.ReadToEndAsync(TestContext.CurrentContext.CancellationToken);

        await using var inputFileReadStream = inputFile.CreateReadStream();
        var parseResult = await _translator.Translate(inputFileReadStream, TestContext.CurrentContext.CancellationToken);

        var patcher = new JsonDiffPatch();
        var diffResult = patcher.Diff((JObject)parseResult.Json, JObject.Parse(outputContents));

        Assert.That(diffResult, Is.Null);
    }

    [Test]
    public async Task Translate_GivenFieldOrderAnnotatedOnTheType_PreservesOrderOnTheType()
    {
        const string idl = "protocol P { record R { @order(\"descending\") int b; } }";

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        var order = result.Json.SelectToken("types[0].fields[0].type.order");
        Assert.That(order?.Value<string>(), Is.EqualTo("descending"));
    }

    [Test]
    public async Task Translate_GivenFieldOrderAnnotatedOnTheField_PreservesOrderOnTheField()
    {
        const string idl = "protocol P { record R { int @order(\"descending\") b; } }";

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        var order = result.Json.SelectToken("types[0].fields[0].order");
        Assert.That(order?.Value<string>(), Is.EqualTo("descending"));
    }

    [Test]
    public async Task Translate_GivenAliasesAnnotatedOnAPrimitiveType_PreservesAliasesOnTheType()
    {
        const string idl = "protocol P { record R { @aliases([\"old\"]) string c; } }";

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        var aliases = result.Json.SelectToken("types[0].fields[0].type.aliases");
        Assert.That(aliases?.Values<string>(), Is.EqualTo(new[] { "old" }));
    }

    [Test]
    public async Task Translate_GivenPropertyAnnotatedOnAReferenceToANamedType_PreservesThePropertyOnTheReference()
    {
        const string idl = "protocol P { record R { int a; } record S { @foo(\"bar\") R h; } }";

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        var fooProperty = result.Json.SelectToken("types[1].fields[0].type.foo");
        Assert.That(fooProperty?.Value<string>(), Is.EqualTo("bar"));
    }

    [Test]
    public async Task Translate_GivenAMessageWithACustomProperty_PreservesThePropertyOnTheMessage()
    {
        const string idl = "protocol P { @x(5) int f(int p); }";

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        var xProperty = result.Json.SelectToken("messages.f.x");
        Assert.That(xProperty?.Value<int>(), Is.EqualTo(5));
    }

    [Test]
    public async Task Translate_GivenAProtocolWithACustomProperty_PreservesThePropertyOnTheProtocol()
    {
        const string idl = "@version(\"1.0\") protocol P { }";

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        var version = result.Json.SelectToken("version");
        Assert.That(version?.Value<string>(), Is.EqualTo("1.0"));
    }

    [Test]
    public async Task Translate_GivenOptionalFieldWithNonNullDefault_EmitsNullLastUnion()
    {
        const string idl = "protocol P { record R { int? i = 3; } }";

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        var type = result.Json.SelectToken("types[0].fields[0].type");
        Assert.That(type?.Values<string>(), Is.EqualTo(new[] { "int", "null" }));
    }

    [Test]
    public async Task Translate_GivenOptionalFieldWithNullDefault_EmitsNullFirstUnion()
    {
        const string idl = "protocol P { record R { int? i = null; } }";

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        var type = result.Json.SelectToken("types[0].fields[0].type");
        Assert.That(type?.Values<string>(), Is.EqualTo(new[] { "null", "int" }));
    }

    [Test]
    public async Task Translate_GivenOptionalFieldWithNoDefault_EmitsNullFirstUnion()
    {
        const string idl = "protocol P { record R { int? i; } }";

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        var type = result.Json.SelectToken("types[0].fields[0].type");
        Assert.That(type?.Values<string>(), Is.EqualTo(new[] { "null", "int" }));
    }

    [Test]
    public async Task Translate_GivenMessageParameterWithOptionalTypeAndNonNullDefault_EmitsNullLastUnion()
    {
        const string idl = "protocol P { void f(int? i = 3); }";

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        var type = result.Json.SelectToken("messages.f.request[0].type");
        Assert.That(type?.Values<string>(), Is.EqualTo(new[] { "int", "null" }));
    }

    [Test]
    public async Task Translate_GivenOptionalFieldWithAnnotation_AttachesAnnotationToTheNonNullBranch()
    {
        const string idl = "protocol P { record R { @logicalType(\"timestamp-micros\") long? ts = null; } }";

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        var type = result.Json.SelectToken("types[0].fields[0].type");
        Assert.That(type?[0]?.Value<string>(), Is.EqualTo("null"));
        Assert.That(type?[1]?["type"]?.Value<string>(), Is.EqualTo("long"));
        Assert.That(type?[1]?["logicalType"]?.Value<string>(), Is.EqualTo("timestamp-micros"));
    }

    [Test]
    public void Translate_GivenAnnotationOnAUnionType_ThrowsInvalidOperationException()
    {
        const string idl = "protocol P { record R { @foo(\"bar\") union { null, string } u; } }";

        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() => _translator.Translate(idl, TestContext.CurrentContext.CancellationToken));

        Assert.That(thrown.Message, Does.Contain("union"));
    }

    [Test]
    public void Translate_GivenTextThatCannotBeTokenised_ThrowsWithPosition()
    {
        const string idl = "protocol TestProtocol { record TestRecord { string a; # } }";

        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() => _translator.Translate(idl, TestContext.CurrentContext.CancellationToken));

        Assert.That(thrown.Message, Does.Contain("Syntax error at line 1:54"));
    }

    [Test]
    public void Translate_GivenStreamThatCannotBeTokenised_ThrowsWithPosition()
    {
        const string idl = "protocol TestProtocol { record TestRecord { string a; # } }";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(idl));

        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() => _translator.Translate(stream, TestContext.CurrentContext.CancellationToken));

        Assert.That(thrown.Message, Does.Contain("Syntax error at line 1:54"));
    }

    [Test]
    public async Task Translate_GivenSelfRecursiveSchemaWithoutSchemaStatement_ReferencesItselfByName()
    {
        const string idl = "namespace s; record Node { string v; array<Node> children; }";

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        var itemsType = result.Json.SelectToken("fields[1].type.items");
        Assert.That(itemsType?.Value<string>(), Is.EqualTo("Node"));
    }

    [Test]
    public async Task Translate_GivenSelfRecursiveSchemaWithSchemaStatement_ReferencesItselfByName()
    {
        const string idl = "namespace s; schema Node; record Node { string v; array<Node> children; }";

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        var itemsType = result.Json.SelectToken("fields[1].type.items");
        Assert.That(itemsType?.Value<string>(), Is.EqualTo("Node"));
    }

    [Test]
    public async Task Translate_GivenMutuallyRecursiveSchemaWithoutSchemaStatement_ReferencesTheOtherTypeByName()
    {
        const string idl = "record A { B b; } record B { A a; }";

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        var innerFieldType = result.Json.SelectToken("fields[0].type.fields[0].type");
        Assert.That(innerFieldType?.Value<string>(), Is.EqualTo("A"));
    }

    [Test]
    public async Task Translate_GivenMutuallyRecursiveSchemaWithSchemaStatement_ReferencesTheOtherTypeByName()
    {
        const string idl = "schema A; record A { B b; } record B { A a; }";

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        var innerFieldType = result.Json.SelectToken("fields[0].type.fields[0].type");
        Assert.That(innerFieldType?.Value<string>(), Is.EqualTo("A"));
    }

    [Test]
    public async Task Translate_GivenNamespacedRecordsReferencingEachOtherByBareName_ResolvesBothAgainstTheirOwnNamespace()
    {
        const string idl = """
            @namespace("a")
            protocol P {
              @namespace("b") record R1 { R2 r; }
              @namespace("b") record R2 { R1 r; }
            }
            """;

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        var inlinedNamespace = result.Json.SelectToken("types[0].fields[0].type.namespace");
        Assert.That(inlinedNamespace?.Value<string>(), Is.EqualTo("b"));

        var innerFieldType = result.Json.SelectToken("types[0].fields[0].type.fields[0].type");
        Assert.That(innerFieldType?.Value<string>(), Is.EqualTo("R1"));
    }

    [Test]
    public async Task Translate_GivenNamespacedRecordReferencingEarlierSiblingByBareName_ResolvesAgainstTheRecordsOwnNamespace()
    {
        const string idl = """
            @namespace("a")
            protocol P {
              @namespace("b") record R1 { string s; }
              @namespace("b") record R2 { R1 r; }
            }
            """;

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        var fieldType = result.Json.SelectToken("types[1].fields[0].type");
        Assert.That(fieldType?.Value<string>(), Is.EqualTo("R1"));
    }

    private static IEnumerable<object[]> IdlInputOutputFilenames()
    {
        var resourceNames = EmbeddedResource.GetEmbeddedResourceNames().ToHashSet(StringComparer.Ordinal);

        return resourceNames
            .Where(n => n.StartsWith(InputResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".avdl", StringComparison.Ordinal))
            .Select(n => n[InputResourcePrefix.Length..])
            .Order(StringComparer.Ordinal)
            .Select(inputName =>
            {
                var candidateOutputNames = Array.ConvertAll(OutputExtensions, ext => Path.ChangeExtension(inputName, ext));
                var outputName = Array.Find(candidateOutputNames, name => resourceNames.Contains(OutputResourcePrefix + name))
                    ?? candidateOutputNames[0];

                return new object[] { inputName, outputName };
            })
            .ToList();
    }
}
