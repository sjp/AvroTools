using Avro;
using NUnit.Framework;

namespace SJP.Avro.Tools.CodeGen.Tests;

[TestFixture]
internal static class AvroEnumGeneratorTests
{
    private const string TestNamespace = "Test.Avro.Namespace";

    [Test]
    public static void Generate_GivenNullSchema_ThrowsArgumentNullException()
    {
        var enumGenerator = new AvroEnumGenerator();

        Assert.That(() => enumGenerator.Generate(default!, TestNamespace), Throws.ArgumentNullException);
    }

    [TestCase((string)null)]
    [TestCase("")]
    [TestCase("    ")]
    public static void Generate_GivenNullOrWhitespaceBaseNamespace_ThrowsArgumentNullException(string baseNamespace)
    {
        var enumGenerator = new AvroEnumGenerator();

        var schema = Schema.Parse(@"{
    ""type"": ""enum"",
    ""name"": ""Position"",
    ""doc"": ""Test documentation"",
    ""namespace"": ""avro.examples.baseball"",
    ""symbols"": [
        ""P"",
        ""C"",
        ""B1"",
        ""B2"",
        ""B3"",
        ""SS"",
        ""LF"",
        ""CF"",
        ""RF"",
        ""DH""
    ]
}") as EnumSchema;

        Assert.That(() => enumGenerator.Generate(schema, baseNamespace), Throws.ArgumentNullException);
    }

    [Test]
    public static void Generate_GivenValidEnumSchema_GeneratesExpectedCode()
    {
        var enumGenerator = new AvroEnumGenerator();

        var schema = Schema.Parse(@"{
    ""type"": ""enum"",
    ""name"": ""Position"",
    ""doc"": ""Test documentation"",
    ""namespace"": ""avro.examples.baseball"",
    ""symbols"": [
        ""P"",
        ""C"",
        ""B1"",
        ""B2"",
        ""B3"",
        ""SS"",
        ""LF"",
        ""CF"",
        ""RF"",
        ""DH""
    ]
}") as EnumSchema;

        var result = enumGenerator.Generate(schema, TestNamespace);

        const string expected = @"namespace avro.examples.baseball
{
    /// <summary>
    /// Test documentation
    /// </summary>
    public enum Position
    {
        P,
        C,
        B1,
        B2,
        B3,
        SS,
        LF,
        CF,
        RF,
        DH
    }
}";

        Assert.That(result, Is.EqualTo(expected).IgnoreLineEndingFormat);
    }

    [Test]
    public static void Generate_GivenEnumSchemaMissingNamespace_GeneratesWithDefaultNamespace()
    {
        var enumGenerator = new AvroEnumGenerator();

        var schema = Schema.Parse(@"{
    ""type"": ""enum"",
    ""name"": ""Position"",
    ""symbols"": [
        ""P"",
        ""C"",
        ""B1"",
        ""B2"",
        ""B3"",
        ""SS"",
        ""LF"",
        ""CF"",
        ""RF"",
        ""DH""
    ]
}") as EnumSchema;

        var result = enumGenerator.Generate(schema, TestNamespace);

        const string expected = @$"namespace {TestNamespace}
{{
    public enum Position
    {{
        P,
        C,
        B1,
        B2,
        B3,
        SS,
        LF,
        CF,
        RF,
        DH
    }}
}}";

        Assert.That(result, Is.EqualTo(expected).IgnoreLineEndingFormat);
    }

    [Test]
    public static void Generate_GivenEnumSchemaWithDefault_OrdersDefaultValueFirst()
    {
        var enumGenerator = new AvroEnumGenerator();

        var schema = Schema.Parse(@"{
    ""type"": ""enum"",
    ""name"": ""Position"",
    ""doc"": ""Test documentation"",
    ""namespace"": ""avro.examples.baseball"",
    ""default"": ""CF"",
    ""symbols"": [
        ""P"",
        ""C"",
        ""B1"",
        ""B2"",
        ""B3"",
        ""SS"",
        ""LF"",
        ""CF"",
        ""RF"",
        ""DH""
    ]
}") as EnumSchema;

        var result = enumGenerator.Generate(schema, TestNamespace);

        const string expected = @"namespace avro.examples.baseball
{
    /// <summary>
    /// Test documentation
    /// </summary>
    public enum Position
    {
        CF,
        P,
        C,
        B1,
        B2,
        B3,
        SS,
        LF,
        RF,
        DH
    }
}";

        Assert.That(result, Is.EqualTo(expected).IgnoreLineEndingFormat);
    }
}