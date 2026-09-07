using System;
using System.Linq;
using System.Threading.Tasks;
using Avro;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SJP.Avro.Tools.Idl;

namespace SJP.Avro.Tools.Tests.Idl;

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

        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() => _translator.Translate(idl, TestContext.CurrentContext.CancellationToken));

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

        var thrown = Assert.ThrowsAsync<FormatException>(() => _translator.Translate(idl, TestContext.CurrentContext.CancellationToken));

        Assert.That(thrown.Message, Does.Contain("32-bit integer"));
    }

    [Test]
    public void Translate_GivenIntegerDefaultTooLargeForAnInt64_ThrowsWithExplanation()
    {
        const string idl = "protocol P { record R { long v = 99999999999999999999; } }";

        var thrown = Assert.ThrowsAsync<FormatException>(() => _translator.Translate(idl, TestContext.CurrentContext.CancellationToken));

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
        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        return result.Match(p => p, _ => throw new InvalidOperationException("Expected a protocol."));
    }
}
