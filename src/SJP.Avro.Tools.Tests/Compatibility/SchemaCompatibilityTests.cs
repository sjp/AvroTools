using System.Linq;
using Avro;
using NUnit.Framework;
using SJP.Avro.Tools.Compatibility;

namespace SJP.Avro.Tools.Tests.Compatibility;

[TestFixture]
internal static class SchemaCompatibilityTests
{
    private static SchemaCompatibilityResult Check(string readerJson, string writerJson)
    {
        var reader = Schema.Parse(readerJson);
        var writer = Schema.Parse(writerJson);
        return SchemaCompatibility.CheckReaderWriterCompatibility(reader, writer);
    }

    [Test]
    public static void Check_GivenIdenticalPrimitives_IsCompatible()
    {
        var result = Check("\"int\"", "\"int\"");
        Assert.That(result.IsCompatible, Is.True);
    }

    // Reader type, writer type, whether the writer promotes to the reader per the Avro spec.
    [TestCase("long", "int", true)]
    [TestCase("float", "int", true)]
    [TestCase("float", "long", true)]
    [TestCase("double", "int", true)]
    [TestCase("double", "long", true)]
    [TestCase("double", "float", true)]
    [TestCase("bytes", "string", true)]
    [TestCase("string", "bytes", true)]
    [TestCase("int", "long", false)]
    [TestCase("float", "double", false)]
    [TestCase("long", "float", false)]
    [TestCase("int", "string", false)]
    [TestCase("boolean", "int", false)]
    public static void Check_GivenPrimitivePromotion_ClassifiesPerSpec(string readerType, string writerType, bool expectedCompatible)
    {
        var result = Check($"\"{readerType}\"", $"\"{writerType}\"");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsCompatible, Is.EqualTo(expectedCompatible));
            if (!expectedCompatible)
                Assert.That(result.Incompatibilities.Single().Type, Is.EqualTo(SchemaIncompatibilityType.TypeMismatch));
        }
    }

    [Test]
    public static void Check_GivenReaderFieldAddedWithoutDefault_ReportsMissingDefault()
    {
        const string writer = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"}]}""";
        const string reader = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"},{"name":"b","type":"int"}]}""";

        var result = Check(reader, writer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsCompatible, Is.False);
            Assert.That(result.Incompatibilities.Single().Type, Is.EqualTo(SchemaIncompatibilityType.ReaderFieldMissingDefaultValue));
        }
    }

    [Test]
    public static void Check_GivenReaderFieldAddedWithDefault_IsCompatible()
    {
        const string writer = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"}]}""";
        const string reader = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"},{"name":"b","type":"int","default":0}]}""";

        var result = Check(reader, writer);
        Assert.That(result.IsCompatible, Is.True);
    }

    [Test]
    public static void Check_GivenWriterFieldNotInReader_IsCompatible()
    {
        const string writer = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"},{"name":"b","type":"int"}]}""";
        const string reader = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"}]}""";

        var result = Check(reader, writer);
        Assert.That(result.IsCompatible, Is.True);
    }

    [Test]
    public static void Check_GivenReaderFieldResolvedByAlias_IsCompatible()
    {
        const string writer = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"}]}""";
        const string reader = """{"type":"record","name":"R","fields":[{"name":"b","type":"int","aliases":["a"]}]}""";

        var result = Check(reader, writer);
        Assert.That(result.IsCompatible, Is.True);
    }

    [Test]
    public static void Check_GivenEnumSymbolMissingFromReader_ReportsMissingSymbols()
    {
        const string writer = """{"type":"enum","name":"E","symbols":["A","B","C"]}""";
        const string reader = """{"type":"enum","name":"E","symbols":["A","B"]}""";

        var result = Check(reader, writer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsCompatible, Is.False);
            Assert.That(result.Incompatibilities.Single().Type, Is.EqualTo(SchemaIncompatibilityType.MissingEnumSymbols));
        }
    }

    [Test]
    public static void Check_GivenEnumSymbolMissingButReaderHasDefault_IsCompatible()
    {
        const string writer = """{"type":"enum","name":"E","symbols":["A","B","C"]}""";
        const string reader = """{"type":"enum","name":"E","symbols":["A","B"],"default":"A"}""";

        var result = Check(reader, writer);
        Assert.That(result.IsCompatible, Is.True);
    }

    [Test]
    public static void Check_GivenWriterUnionBranchNotReadable_ReportsMissingUnionBranch()
    {
        const string writer = """["null","int","string"]""";
        const string reader = """["null","int"]""";

        var result = Check(reader, writer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsCompatible, Is.False);
            Assert.That(result.Incompatibilities.Single().Type, Is.EqualTo(SchemaIncompatibilityType.MissingUnionBranch));
        }
    }

    [Test]
    public static void Check_GivenNonUnionReaderPromotableFromUnionBranch_ReportsMissingBranchForOthers()
    {
        // Reader is a bare long; writer may emit int (promotable) or string (not) -> incompatible.
        var result = Check("\"long\"", """["int","string"]""");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsCompatible, Is.False);
            Assert.That(result.Incompatibilities.Single().Type, Is.EqualTo(SchemaIncompatibilityType.TypeMismatch));
        }
    }

    [Test]
    public static void Check_GivenFixedSizeMismatch_ReportsFixedSizeMismatch()
    {
        const string writer = """{"type":"fixed","name":"F","size":8}""";
        const string reader = """{"type":"fixed","name":"F","size":16}""";

        var result = Check(reader, writer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsCompatible, Is.False);
            Assert.That(result.Incompatibilities.Single().Type, Is.EqualTo(SchemaIncompatibilityType.FixedSizeMismatch));
        }
    }

    [Test]
    public static void Check_GivenRenamedRecordWithoutAlias_ReportsNameMismatch()
    {
        const string writer = """{"type":"record","name":"Old","fields":[{"name":"a","type":"int"}]}""";
        const string reader = """{"type":"record","name":"New","fields":[{"name":"a","type":"int"}]}""";

        var result = Check(reader, writer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsCompatible, Is.False);
            Assert.That(result.Incompatibilities.Single().Type, Is.EqualTo(SchemaIncompatibilityType.NameMismatch));
        }
    }

    [Test]
    public static void Check_GivenRenamedRecordWithMatchingAlias_IsCompatible()
    {
        const string writer = """{"type":"record","name":"Old","fields":[{"name":"a","type":"int"}]}""";
        const string reader = """{"type":"record","name":"New","aliases":["Old"],"fields":[{"name":"a","type":"int"}]}""";

        var result = Check(reader, writer);
        Assert.That(result.IsCompatible, Is.True);
    }

    [Test]
    public static void Check_GivenNestedFieldPromotion_RecursesIntoRecords()
    {
        const string writer = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"}]}""";
        const string reader = """{"type":"record","name":"R","fields":[{"name":"a","type":"long"}]}""";

        var result = Check(reader, writer);
        Assert.That(result.IsCompatible, Is.True);
    }

    [Test]
    public static void Check_GivenArrayItemMismatch_ReportsTypeMismatchAtItems()
    {
        var result = Check("""{"type":"array","items":"int"}""", """{"type":"array","items":"string"}""");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsCompatible, Is.False);
            Assert.That(result.Incompatibilities.Single().Type, Is.EqualTo(SchemaIncompatibilityType.TypeMismatch));
            Assert.That(result.Incompatibilities.Single().Location, Does.Contain("items"));
        }
    }

    [Test]
    public static void Check_GivenMapValuePromotion_IsCompatible()
    {
        var result = Check("""{"type":"map","values":"long"}""", """{"type":"map","values":"int"}""");
        Assert.That(result.IsCompatible, Is.True);
    }

    [Test]
    public static void Check_GivenRecursiveSchema_TerminatesAndIsCompatible()
    {
        const string node = """{"type":"record","name":"Node","fields":[{"name":"next","type":["null","Node"],"default":null}]}""";

        var result = Check(node, node);
        Assert.That(result.IsCompatible, Is.True);
    }
}
