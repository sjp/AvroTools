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

        var content = await File.ReadAllTextAsync(main, TestContext.CurrentContext.CancellationToken);
        var result = await _translator.Translate(content, Path.GetDirectoryName(main), TestContext.CurrentContext.CancellationToken);

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
        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        return result.Match(p => p, _ => throw new InvalidOperationException("Expected a protocol."));
    }
}
