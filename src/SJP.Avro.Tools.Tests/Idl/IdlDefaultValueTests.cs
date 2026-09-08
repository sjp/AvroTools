using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SJP.Avro.Tools.Idl;

namespace SJP.Avro.Tools.Tests.Idl;

[TestFixture]
internal class IdlDefaultValueTests
{
    private IdlToAvroTranslator _translator;

    [SetUp]
    public void Setup()
    {
        _translator = new IdlToAvroTranslator(new PhysicalIdlFileReader());
    }

    [Test]
    public void Translate_GivenAnIntegerDefaultTooLargeForAnInt_ReportsTheField()
    {
        const string idl = "protocol P { record R { int x = 2147483648; } }";

        var thrown = Assert.ThrowsAsync<IdlTranslationException>(() => _translator.Translate(idl, TestContext.CurrentContext.CancellationToken));

        Assert.That(thrown.Message, Does.Contain("field 'x'").And.Contain("outside the range of \"int\""));
    }

    [Test]
    public void Translate_GivenAUnionDefaultMatchingALaterBranch_ReportsTheFirstBranchRule()
    {
        const string idl = "protocol P { record R { union { null, int } i = 3; } }";

        var thrown = Assert.ThrowsAsync<IdlTranslationException>(() => _translator.Translate(idl, TestContext.CurrentContext.CancellationToken));

        Assert.That(thrown.Message, Does.Contain("field 'i'").And.Contain("first branch"));
    }

    [Test]
    public void Translate_GivenADefaultOfTheWrongPrimitiveType_ReportsTheField()
    {
        const string idl = "protocol P { record R { int x = \"one\"; } }";

        var thrown = Assert.ThrowsAsync<IdlTranslationException>(() => _translator.Translate(idl, TestContext.CurrentContext.CancellationToken));

        Assert.That(thrown.Message, Does.Contain("field 'x'").And.Contain("not a valid \"int\" value"));
    }

    [Test]
    public void Translate_GivenAnEnumDefaultThatIsNotAString_ReportsTheField()
    {
        const string idl = "protocol P { enum E { A, B } record R { E e = 3; } }";

        var thrown = Assert.ThrowsAsync<IdlTranslationException>(() => _translator.Translate(idl, TestContext.CurrentContext.CancellationToken));

        Assert.That(thrown.Message, Does.Contain("field 'e'").And.Contain("'E'"));
    }

    [Test]
    public void Translate_GivenAnArrayDefaultHoldingTheWrongElement_ReportsTheElement()
    {
        const string idl = "protocol P { record R { array<int> a = [1, \"two\"]; } }";

        var thrown = Assert.ThrowsAsync<IdlTranslationException>(() => _translator.Translate(idl, TestContext.CurrentContext.CancellationToken));

        Assert.That(thrown.Message, Does.Contain("an element of the array").And.Contain("not a valid \"int\" value"));
    }

    [Test]
    public void Translate_GivenAMapDefaultHoldingTheWrongValue_ReportsTheEntry()
    {
        const string idl = "protocol P { record R { map<int> m = {\"a\": true}; } }";

        var thrown = Assert.ThrowsAsync<IdlTranslationException>(() => _translator.Translate(idl, TestContext.CurrentContext.CancellationToken));

        Assert.That(thrown.Message, Does.Contain("map entry 'a'").And.Contain("not a valid \"int\" value"));
    }

    [Test]
    public void Translate_GivenARecordDefaultMissingAFieldWithNoDefaultOfItsOwn_ReportsThatField()
    {
        const string idl = "protocol P { record Inner { int a; int b; } record R { Inner i = {\"a\": 1}; } }";

        var thrown = Assert.ThrowsAsync<IdlTranslationException>(() => _translator.Translate(idl, TestContext.CurrentContext.CancellationToken));

        Assert.That(thrown.Message, Does.Contain("no value was given for field 'b'"));
    }

    [Test]
    public void Translate_GivenARecordDefaultHoldingTheWrongFieldValue_ReportsThatField()
    {
        const string idl = "protocol P { record Inner { int a; } record R { Inner i = {\"a\": \"one\"}; } }";

        var thrown = Assert.ThrowsAsync<IdlTranslationException>(() => _translator.Translate(idl, TestContext.CurrentContext.CancellationToken));

        Assert.That(thrown.Message, Does.Contain("for field 'a' of 'Inner'").And.Contain("not a valid \"int\" value"));
    }

    [Test]
    public void Translate_GivenARecordDefaultAgainstATypeDeclaredLater_StillValidatesIt()
    {
        const string idl = "protocol P { record R { Later l = {\"a\": \"one\"}; } record Later { int a; } }";

        var thrown = Assert.ThrowsAsync<IdlTranslationException>(() => _translator.Translate(idl, TestContext.CurrentContext.CancellationToken));

        Assert.That(thrown.Message, Does.Contain("for field 'a' of 'Later'"));
    }

    [Test]
    public void Translate_GivenAMessageParameterDefaultTheTypeCannotHold_ReportsTheParameter()
    {
        const string idl = "protocol P { int add(int arg = \"one\"); }";

        var thrown = Assert.ThrowsAsync<IdlTranslationException>(() => _translator.Translate(idl, TestContext.CurrentContext.CancellationToken));

        Assert.That(thrown.Message, Does.Contain("parameter 'arg' of message 'add'"));
    }

    [Test]
    public async Task Translate_GivenAnOptionalTypeWithANonNullDefault_AcceptsTheReorderedUnion()
    {
        const string idl = "protocol P { record R { int? x = 3; } }";

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Json.SelectToken("types[0].fields[0].type[0]")?.Value<string>(), Is.EqualTo("int"));
    }

    [TestCase("double d = 1;")]
    [TestCase("float f = 1;")]
    [TestCase("long l = 1;")]
    [TestCase("bytes b = \"\\u00ff\";")]
    [TestCase("array<int> a = [];")]
    [TestCase("map<int> m = {};")]
    [TestCase("union { null, int } u = null;")]
    [TestCase("string? s = null;")]
    public async Task Translate_GivenADefaultTheTypeCanHold_AcceptsIt(string declaration)
    {
        var idl = $"protocol P {{ record R {{ {declaration} }} }}";

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Json.SelectToken("types[0].fields[0].default"), Is.Not.Null);
    }
}
