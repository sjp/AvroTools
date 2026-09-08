using Avro;
using NUnit.Framework;

namespace SJP.Avro.Tools.CodeGen.Tests;

[TestFixture]
internal static class AvroFixedGeneratorTests
{
    private const string TestNamespace = "Test.Avro.Namespace";

    [Test]
    public static void Generate_GivenNullSchema_ThrowsArgumentNullException()
    {
        var fixedGenerator = new AvroFixedGenerator();

        Assert.That(() => fixedGenerator.Generate(default!, TestNamespace), Throws.ArgumentNullException);
    }

    [Test]
    public static void Generate_GivenNullBaseNamespace_ThrowsArgumentNullException()
    {
        var fixedGenerator = new AvroFixedGenerator();

        var schema = Schema.Parse("""
{
    "type": "fixed",
    "name": "MD5",
    "doc": "An MD5 hash.",
    "namespace": "org.apache.avro.test",
    "size": 16,
    "foo": "bar"
}
""") as FixedSchema;

        Assert.That(() => fixedGenerator.Generate(schema, null), Throws.ArgumentNullException);
    }

    [TestCase("")]
    [TestCase("    ")]
    public static void Generate_GivenEmptyOrWhitespaceBaseNamespaceAndSchemaWithoutNamespace_ThrowsArgumentException(string baseNamespace)
    {
        var fixedGenerator = new AvroFixedGenerator();

        var schema = Schema.Parse("""
{
    "type": "fixed",
    "name": "MD5",
    "doc": "An MD5 hash.",
    "size": 16,
    "foo": "bar"
}
""") as FixedSchema;

        Assert.That(() => fixedGenerator.Generate(schema, baseNamespace), Throws.ArgumentException);
    }

    [TestCase("")]
    [TestCase("    ")]
    public static void Generate_GivenEmptyOrWhitespaceBaseNamespaceAndSchemaWithNamespace_UsesTheSchemaNamespace(string baseNamespace)
    {
        var fixedGenerator = new AvroFixedGenerator();

        var schema = Schema.Parse("""
{
    "type": "fixed",
    "name": "MD5",
    "doc": "An MD5 hash.",
    "namespace": "org.apache.avro.test",
    "size": 16,
    "foo": "bar"
}
""") as FixedSchema;

        var result = fixedGenerator.Generate(schema, baseNamespace);

        Assert.That(result, Does.Contain("namespace org.apache.avro.test"));
    }

    [Test]
    public static void Generate_GivenValidFixedSchema_GeneratesExpectedCode()
    {
        var fixedGenerator = new AvroFixedGenerator();

        var schema = Schema.Parse("""
{
    "type": "fixed",
    "name": "MD5",
    "doc": "An MD5 hash.",
    "namespace": "org.apache.avro.test",
    "size": 16,
    "foo": "bar"
}
""") as FixedSchema;

        var result = fixedGenerator.Generate(schema, TestNamespace);

        const string expected = """
namespace org.apache.avro.test
{
    /// <summary>
    /// An MD5 hash.
    /// </summary>
    public class MD5 : global::Avro.Specific.SpecificFixed
    {
        private static readonly global::Avro.Schema _schema = global::Avro.Schema.Parse("{\"type\":\"fixed\",\"name\":\"MD5\",\"doc\":\"An MD5 hash.\",\"namespace\":\"org.apache.avro.test\",\"size\":16,\"foo\":\"bar\"}");

        public override global::Avro.Schema Schema { get; } = _schema;

        public static uint FixedSize { get; } = 16;

        public MD5() : base(FixedSize)
        {
        }
    }
}
""";

        Assert.That(result, Is.EqualTo(expected).IgnoreLineEndingFormat);
    }

    [Test]
    public static void Generate_GivenFixedSchemaWithoutNamespace_GeneratesCodeWithDefaultNamespace()
    {
        var fixedGenerator = new AvroFixedGenerator();

        var schema = Schema.Parse("""
{
    "type": "fixed",
    "name": "MD5",
    "size": 16,
    "foo": "bar"
}
""") as FixedSchema;

        var result = fixedGenerator.Generate(schema, TestNamespace);

        const string expected = $$"""
namespace {{TestNamespace}}
{
    public class MD5 : global::Avro.Specific.SpecificFixed
    {
        private static readonly global::Avro.Schema _schema = global::Avro.Schema.Parse("{\"type\":\"fixed\",\"name\":\"MD5\",\"size\":16,\"foo\":\"bar\"}");

        public override global::Avro.Schema Schema { get; } = _schema;

        public static uint FixedSize { get; } = 16;

        public MD5() : base(FixedSize)
        {
        }
    }
}
""";

        Assert.That(result, Is.EqualTo(expected).IgnoreLineEndingFormat);
    }

    [Test]
    public static void Generate_GivenEmptyDocumentation_GeneratesCodeWithoutDocComments()
    {
        var fixedGenerator = new AvroFixedGenerator();

        var schema = (FixedSchema)Schema.Parse("""
{
    "type": "fixed",
    "name": "MD5",
    "doc": "",
    "namespace": "org.apache.avro.test",
    "size": 16
}
""");

        var result = fixedGenerator.Generate(schema, TestNamespace);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("public class MD5 : global::Avro.Specific.SpecificFixed"));
            Assert.That(result, Does.Not.Contain("<summary>"));
        }
    }
}
