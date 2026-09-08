using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SJP.Avro.Tools.Idl;

namespace SJP.Avro.Tools.Tests.Idl;

[TestFixture]
internal class EscapedNameTests
{
    private IdlToAvroTranslator _translator;

    [SetUp]
    public void Setup()
    {
        _translator = new IdlToAvroTranslator(new PhysicalIdlFileReader());
    }

    [Test]
    public async Task Translate_GivenEnumWithEscapedSymbols_RemovesEscapingBackticks()
    {
        var enumJson = await GetEnum("protocol P { enum E { `error`, `custom`, X } }");

        Assert.That(enumJson["symbols"]!.Values<string>(), Is.EqualTo(new[] { "error", "custom", "X" }));
    }

    [Test]
    public async Task Translate_GivenEnumWithEscapedDefault_RemovesEscapingBackticks()
    {
        var enumJson = await GetEnum("protocol P { enum E { `error`, X } = `error`; }");

        Assert.That(enumJson["default"]!.ToString(), Is.EqualTo("error"));
    }

    [Test]
    public async Task Translate_GivenEnumWithPlainDefault_UsesTheDefaultSymbol()
    {
        var enumJson = await GetEnum("protocol P { enum E { A, B } = B; }");

        Assert.That(enumJson["default"]!.ToString(), Is.EqualTo("B"));
    }

    [Test]
    public async Task Translate_GivenEnumWithoutDefault_OmitsTheDefault()
    {
        var enumJson = await GetEnum("protocol P { enum E { A, B } }");

        Assert.That(enumJson["default"], Is.Null);
    }

    [Test]
    public async Task Translate_GivenEscapedNameThatIsNotAKeyword_RemovesEscapingBackticks()
    {
        var protocolJson = await TranslateToJson("protocol P { record R { string `count`; } }");
        var record = protocolJson["types"]!.First(t => t["name"]!.ToString() == "R");

        Assert.That(record["fields"]![0]!["name"]!.ToString(), Is.EqualTo("count"));
    }

    [Test]
    public async Task Translate_GivenEscapedPropertyNameWithMultipleParts_RemovesEscapingBackticksFromEveryPart()
    {
        var protocolJson = await TranslateToJson("""protocol P { @`my`.`prop`("v") record R { string a; } }""");
        var record = protocolJson["types"]!.First(t => t["name"]!.ToString() == "R");

        Assert.That(record["my.prop"]!.ToString(), Is.EqualTo("v"));
    }

    [Test]
    public async Task Translate_GivenEscapedProtocolName_RemovesEscapingBackticks()
    {
        var protocolJson = await TranslateToJson("protocol `error` { record R { string a; } }");

        Assert.That(protocolJson["protocol"]!.ToString(), Is.EqualTo("error"));
    }

    [Test]
    public async Task Translate_GivenEscapedNamespaceDeclaration_RemovesEscapingBackticks()
    {
        var schemaJson = await TranslateSchemaToJson("namespace `record`.example; record R { string a; }");

        Assert.That(schemaJson["namespace"]!.ToString(), Is.EqualTo("record.example"));
    }

    [Test]
    public async Task Translate_GivenEscapedRecordName_RemovesEscapingBackticks()
    {
        var protocolJson = await TranslateToJson("protocol P { record `record` { string a; } }");

        Assert.That(protocolJson["types"]![0]!["name"]!.ToString(), Is.EqualTo("record"));
    }

    [Test]
    public async Task Translate_GivenEscapedFixedName_RemovesEscapingBackticks()
    {
        var protocolJson = await TranslateToJson("protocol P { fixed `fixed`(16); }");

        Assert.That(protocolJson["types"]![0]!["name"]!.ToString(), Is.EqualTo("fixed"));
    }

    [Test]
    public async Task Translate_GivenEscapedReferenceToDeclaredType_ReferencesItByName()
    {
        var protocolJson = await TranslateToJson("protocol P { record `record` { string a; } record S { `record` r; } }");
        var s = protocolJson["types"]!.First(t => t["name"]!.ToString() == "S");

        Assert.Multiple(() =>
        {
            Assert.That(protocolJson["types"]!.Count(), Is.EqualTo(2));
            Assert.That(s["fields"]![0]!["type"]!.ToString(), Is.EqualTo("record"));
        });
    }

    [Test]
    public async Task Translate_GivenEscapedForwardReferenceToDeclaredType_InlinesASingleDefinition()
    {
        var protocolJson = await TranslateToJson("protocol P { record S { `record` r; } record `record` { string a; } }");
        var s = protocolJson["types"]!.First(t => t["name"]!.ToString() == "S");

        Assert.Multiple(() =>
        {
            Assert.That(protocolJson["types"]!.Count(), Is.EqualTo(1));
            Assert.That(s["fields"]![0]!["type"]!["name"]!.ToString(), Is.EqualTo("record"));
        });
    }

    [Test]
    public async Task Translate_GivenEscapedNameInThrowsClause_RemovesEscapingBackticks()
    {
        var protocolJson = await TranslateToJson("protocol P { error `TestError` { string msg; } void f() throws `TestError`; }");

        Assert.That(protocolJson["messages"]!["f"]!["errors"]!.Values<string>(), Is.EqualTo(new[] { "TestError" }));
    }

    [Test]
    public async Task Translate_GivenEscapedMessageName_RemovesEscapingBackticks()
    {
        var protocolJson = await TranslateToJson("protocol P { void `void`(); }");

        Assert.That(protocolJson["messages"]!["void"], Is.Not.Null);
    }

    [Test]
    public async Task Translate_GivenEscapedParameterName_RemovesEscapingBackticks()
    {
        var protocolJson = await TranslateToJson("protocol P { void f(string `string`); }");

        Assert.That(protocolJson["messages"]!["f"]!["request"]![0]!["name"]!.ToString(), Is.EqualTo("string"));
    }

    private async Task<JObject> TranslateSchemaToJson(string idl)
    {
        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);
        var schema = result.Match(_ => throw new InvalidOperationException("Expected a schema."), s => s);

        return JObject.Parse(schema.ToString());
    }

    private async Task<JToken> GetEnum(string idl)
    {
        var protocolJson = await TranslateToJson(idl);

        return protocolJson["types"]!.First(t => t["type"]!.ToString() == "enum");
    }

    private async Task<JObject> TranslateToJson(string idl)
    {
        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);
        var protocol = result.Match(p => p, _ => throw new InvalidOperationException("Expected a protocol."));

        return JObject.Parse(protocol.ToString());
    }
}
