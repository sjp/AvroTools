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
