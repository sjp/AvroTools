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

    [Test]
    public static void Generate_GivenNullBaseNamespace_ThrowsArgumentNullException()
    {
        var enumGenerator = new AvroEnumGenerator();

        var schema = Schema.Parse("""
{
    "type": "enum",
    "name": "Position",
    "doc": "Test documentation",
    "namespace": "avro.examples.baseball",
    "symbols": [
        "P",
        "C",
        "B1",
        "B2",
        "B3",
        "SS",
        "LF",
        "CF",
        "RF",
        "DH"
    ]
}
""") as EnumSchema;

        Assert.That(() => enumGenerator.Generate(schema, null), Throws.ArgumentNullException);
    }

    [TestCase("")]
    [TestCase("    ")]
    public static void Generate_GivenEmptyOrWhitespaceBaseNamespaceAndSchemaWithoutNamespace_ThrowsArgumentException(string baseNamespace)
    {
        var enumGenerator = new AvroEnumGenerator();

        var schema = Schema.Parse("""
{
    "type": "enum",
    "name": "Position",
    "doc": "Test documentation",
    "symbols": [
        "P",
        "C",
        "B1",
        "B2",
        "B3",
        "SS",
        "LF",
        "CF",
        "RF",
        "DH"
    ]
}
""") as EnumSchema;

        Assert.That(() => enumGenerator.Generate(schema, baseNamespace), Throws.ArgumentException);
    }

    [TestCase("")]
    [TestCase("    ")]
    public static void Generate_GivenEmptyOrWhitespaceBaseNamespaceAndSchemaWithNamespace_UsesTheSchemaNamespace(string baseNamespace)
    {
        var enumGenerator = new AvroEnumGenerator();

        var schema = Schema.Parse("""
{
    "type": "enum",
    "name": "Position",
    "doc": "Test documentation",
    "namespace": "avro.examples.baseball",
    "symbols": [
        "P",
        "C",
        "B1",
        "B2",
        "B3",
        "SS",
        "LF",
        "CF",
        "RF",
        "DH"
    ]
}
""") as EnumSchema;

        var result = enumGenerator.Generate(schema, baseNamespace);

        Assert.That(result, Does.Contain("namespace avro.examples.baseball"));
    }

    [Test]
    public static void Generate_GivenValidEnumSchema_GeneratesExpectedCode()
    {
        var enumGenerator = new AvroEnumGenerator();

        var schema = Schema.Parse("""
{
    "type": "enum",
    "name": "Position",
    "doc": "Test documentation",
    "namespace": "avro.examples.baseball",
    "symbols": [
        "P",
        "C",
        "B1",
        "B2",
        "B3",
        "SS",
        "LF",
        "CF",
        "RF",
        "DH"
    ]
}
""") as EnumSchema;

        var result = enumGenerator.Generate(schema, TestNamespace);

        const string expected = @"namespace avro.examples.baseball
{
    /// <summary>
    /// Test documentation
    /// </summary>
    public enum Position
    {
        P = 0,
        C = 1,
        B1 = 2,
        B2 = 3,
        B3 = 4,
        SS = 5,
        LF = 6,
        CF = 7,
        RF = 8,
        DH = 9
    }
}";

        Assert.That(result, Is.EqualTo(expected).IgnoreLineEndingFormat);
    }

    [Test]
    public static void Generate_GivenEnumSchemaMissingNamespace_GeneratesWithDefaultNamespace()
    {
        var enumGenerator = new AvroEnumGenerator();

        var schema = Schema.Parse("""
{
    "type": "enum",
    "name": "Position",
    "symbols": [
        "P",
        "C",
        "B1",
        "B2",
        "B3",
        "SS",
        "LF",
        "CF",
        "RF",
        "DH"
    ]
}
""") as EnumSchema;

        var result = enumGenerator.Generate(schema, TestNamespace);

        const string expected = @$"namespace {TestNamespace}
{{
    public enum Position
    {{
        P = 0,
        C = 1,
        B1 = 2,
        B2 = 3,
        B3 = 4,
        SS = 5,
        LF = 6,
        CF = 7,
        RF = 8,
        DH = 9
    }}
}}";

        Assert.That(result, Is.EqualTo(expected).IgnoreLineEndingFormat);
    }

    [Test]
    public static void Generate_GivenEnumSchemaWithDefault_KeepsSchemaSymbolOrder()
    {
        var enumGenerator = new AvroEnumGenerator();

        var schema = Schema.Parse("""
{
    "type": "enum",
    "name": "Position",
    "doc": "Test documentation",
    "namespace": "avro.examples.baseball",
    "default": "CF",
    "symbols": [
        "P",
        "C",
        "B1",
        "B2",
        "B3",
        "SS",
        "LF",
        "CF",
        "RF",
        "DH"
    ]
}
""") as EnumSchema;

        var result = enumGenerator.Generate(schema, TestNamespace);

        const string expected = @"namespace avro.examples.baseball
{
    /// <summary>
    /// Test documentation
    /// </summary>
    public enum Position
    {
        P = 0,
        C = 1,
        B1 = 2,
        B2 = 3,
        B3 = 4,
        SS = 5,
        LF = 6,
        CF = 7,
        RF = 8,
        DH = 9
    }
}";

        Assert.That(result, Is.EqualTo(expected).IgnoreLineEndingFormat);
    }

    [Test]
    public static void Generate_GivenEmptyDocumentation_GeneratesCodeWithoutDocComments()
    {
        var enumGenerator = new AvroEnumGenerator();

        var schema = (EnumSchema)Schema.Parse("""
{
    "type": "enum",
    "name": "Position",
    "doc": "  ",
    "namespace": "avro.examples.baseball",
    "symbols": [ "P", "C" ]
}
""");

        var result = enumGenerator.Generate(schema, TestNamespace);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("public enum Position"));
            Assert.That(result, Does.Not.Contain("<summary>"));
        }
    }
}
