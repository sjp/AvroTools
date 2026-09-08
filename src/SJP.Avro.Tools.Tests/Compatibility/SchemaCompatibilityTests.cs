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
            {
                Assert.That(result.Incompatibilities.Single().Type, Is.EqualTo(SchemaIncompatibilityType.TypeMismatch));
                Assert.That(
                    result.Incompatibilities.Single().Message,
                    Is.EqualTo($"reader type: {readerType} not compatible with writer type: {writerType}"));
            }
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
            Assert.That(result.Incompatibilities.Single().Message, Is.EqualTo("reader union lacking writer type: string"));
        }
    }

    [Test]
    public static void Check_GivenWriterUnionBranchOfNamedTypeNotReadable_NamesTheMissingType()
    {
        const string writer = """["null",{"type":"record","name":"B","namespace":"ns","fields":[]}]""";
        const string reader = """["null",{"type":"record","name":"A","namespace":"ns","fields":[]}]""";

        var result = Check(reader, writer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsCompatible, Is.False);
            Assert.That(result.Incompatibilities.Single().Type, Is.EqualTo(SchemaIncompatibilityType.MissingUnionBranch));
            Assert.That(result.Incompatibilities.Single().Message, Is.EqualTo("reader union lacking writer type: record ns.B"));
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
    public static void Check_GivenRecordMovedToAnotherNamespaceWithQualifiedAlias_IsCompatible()
    {
        const string writer = """{"type":"record","name":"R","namespace":"v1","fields":[{"name":"a","type":"int"}]}""";
        const string reader = """{"type":"record","name":"R","namespace":"v2","aliases":["v1.R"],"fields":[{"name":"a","type":"int"}]}""";

        var result = Check(reader, writer);
        Assert.That(result.IsCompatible, Is.True);
    }

    [Test]
    public static void Check_GivenRenamedRecordWithBareAliasInSameNamespace_IsCompatible()
    {
        const string writer = """{"type":"record","name":"Old","namespace":"ns","fields":[{"name":"a","type":"int"}]}""";
        const string reader = """{"type":"record","name":"New","namespace":"ns","aliases":["Old"],"fields":[{"name":"a","type":"int"}]}""";

        var result = Check(reader, writer);
        Assert.That(result.IsCompatible, Is.True);
    }

    [Test]
    public static void Check_GivenRenamedRecordWithBareAliasInAnotherNamespace_ReportsNameMismatch()
    {
        const string writer = """{"type":"record","name":"Old","namespace":"other","fields":[{"name":"a","type":"int"}]}""";
        const string reader = """{"type":"record","name":"New","namespace":"ns","aliases":["Old"],"fields":[{"name":"a","type":"int"}]}""";

        var result = Check(reader, writer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsCompatible, Is.False);
            Assert.That(result.Incompatibilities.Single().Type, Is.EqualTo(SchemaIncompatibilityType.NameMismatch));
        }
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

    [Test]
    public static void Check_GivenNestedFieldMismatch_LocatesItUnderTheFieldsType()
    {
        const string writer = """{"type":"record","name":"R","fields":[{"name":"a","type":{"type":"array","items":"string"}}]}""";
        const string reader = """{"type":"record","name":"R","fields":[{"name":"a","type":{"type":"array","items":"int"}}]}""";

        var result = Check(reader, writer);

        Assert.That(result.Incompatibilities.Single().Location, Is.EqualTo("/fields/a/type/items"));
    }

    [Test]
    public static void Check_GivenTypeUsedByTwoFields_ReportsIncompatibilityAtEachFieldsLocation()
    {
        const string writer = """
        {
            "type": "record",
            "name": "Person",
            "fields": [
                { "name": "home", "type": { "type": "record", "name": "Address", "fields": [{ "name": "street", "type": "string" }] } },
                { "name": "work", "type": "Address" }
            ]
        }
        """;
        const string reader = """
        {
            "type": "record",
            "name": "Person",
            "fields": [
                {
                    "name": "home",
                    "type": {
                        "type": "record",
                        "name": "Address",
                        "fields": [{ "name": "street", "type": "string" }, { "name": "zip", "type": "string" }]
                    }
                },
                { "name": "work", "type": "Address" }
            ]
        }
        """;

        var result = Check(reader, writer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Incompatibilities.Select(i => i.Type),
                Is.All.EqualTo(SchemaIncompatibilityType.ReaderFieldMissingDefaultValue));
            Assert.That(result.Incompatibilities.Select(i => i.Location), Is.EquivalentTo(new[]
            {
                "/fields/home/type/fields/zip",
                "/fields/work/type/fields/zip",
            }));
        }
    }

    [Test]
    public static void Check_GivenTypeUsedByAFieldAndAnArray_ReportsIncompatibilityAtEachLocation()
    {
        const string writer = """
        {
            "type": "record",
            "name": "Person",
            "fields": [
                { "name": "home", "type": { "type": "record", "name": "Address", "fields": [{ "name": "street", "type": "string" }] } },
                { "name": "previous", "type": { "type": "array", "items": "Address" } }
            ]
        }
        """;
        const string reader = """
        {
            "type": "record",
            "name": "Person",
            "fields": [
                {
                    "name": "home",
                    "type": {
                        "type": "record",
                        "name": "Address",
                        "fields": [{ "name": "street", "type": "string" }, { "name": "zip", "type": "string" }]
                    }
                },
                { "name": "previous", "type": { "type": "array", "items": "Address" } }
            ]
        }
        """;

        var result = Check(reader, writer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Incompatibilities.Select(i => i.Type),
                Is.All.EqualTo(SchemaIncompatibilityType.ReaderFieldMissingDefaultValue));
            Assert.That(result.Incompatibilities.Select(i => i.Location), Is.EquivalentTo(new[]
            {
                "/fields/home/type/fields/zip",
                "/fields/previous/type/items/fields/zip",
            }));
        }
    }

    [Test]
    public static void Check_GivenTypeReachedThroughRecursion_ReportsIncompatibilityAtEachLocation()
    {
        // "X" is first reached inside "B", where the walk stops at the recursion back into "B" and
        // so cannot see the mismatch below it. Reached again as Root's own field, where "B" is not
        // being walked, everything under it must still be reported.
        const string writer = """
        {
            "type": "record",
            "name": "Root",
            "fields": [
                {
                    "name": "b",
                    "type": {
                        "type": "record",
                        "name": "B",
                        "fields": [
                            { "name": "a", "type": { "type": "record", "name": "X", "fields": [{ "name": "r", "type": "B" }] } },
                            { "name": "d", "type": "string" }
                        ]
                    }
                },
                { "name": "x", "type": "X" }
            ]
        }
        """;
        const string reader = """
        {
            "type": "record",
            "name": "Root",
            "fields": [
                {
                    "name": "b",
                    "type": {
                        "type": "record",
                        "name": "B",
                        "fields": [
                            { "name": "a", "type": { "type": "record", "name": "X", "fields": [{ "name": "r", "type": "B" }] } },
                            { "name": "d", "type": "int" }
                        ]
                    }
                },
                { "name": "x", "type": "X" }
            ]
        }
        """;

        var result = Check(reader, writer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Incompatibilities.Select(i => i.Type),
                Is.All.EqualTo(SchemaIncompatibilityType.TypeMismatch));
            Assert.That(result.Incompatibilities.Select(i => i.Location), Is.EquivalentTo(new[]
            {
                "/fields/b/type/fields/d/type",
                "/fields/x/type/fields/r/type/fields/d/type",
            }));
        }
    }

    [Test]
    public static void Check_GivenReaderRecordAndWriterErrorOfSameShape_IsCompatible()
    {
        const string writer = """{"type":"error","name":"R","fields":[{"name":"a","type":"int"}]}""";
        const string reader = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"}]}""";

        var result = Check(reader, writer);
        Assert.That(result.IsCompatible, Is.True);
    }

    [Test]
    public static void Check_GivenReaderErrorAndWriterRecordOfSameShape_IsCompatible()
    {
        const string writer = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"}]}""";
        const string reader = """{"type":"error","name":"R","fields":[{"name":"a","type":"int"}]}""";

        var result = Check(reader, writer);
        Assert.That(result.IsCompatible, Is.True);
    }

    [Test]
    public static void Check_GivenReaderRecordAndWriterErrorWithAddedField_ReportsTheFieldNotTheType()
    {
        const string writer = """{"type":"error","name":"R","fields":[{"name":"a","type":"int"}]}""";
        const string reader = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"},{"name":"b","type":"int"}]}""";

        var result = Check(reader, writer);
        var incompatibility = result.Incompatibilities.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(incompatibility.Type, Is.EqualTo(SchemaIncompatibilityType.ReaderFieldMissingDefaultValue));
            Assert.That(incompatibility.Location, Is.EqualTo("/fields/b"));
        }
    }

    [Test]
    public static void Check_GivenReaderEnumAndWriterErrorOfSameName_ReportsTypeMismatch()
    {
        const string writer = """{"type":"error","name":"R","fields":[{"name":"a","type":"int"}]}""";
        const string reader = """{"type":"enum","name":"R","symbols":["A"]}""";

        var result = Check(reader, writer);
        Assert.That(result.Incompatibilities.Single().Type, Is.EqualTo(SchemaIncompatibilityType.TypeMismatch));
    }

    [Test]
    public static void Check_GivenReaderFieldAddedWithAnExplicitNullDefault_IsCompatible()
    {
        // A JSON null is a default value like any other: it is what the reader populates the field
        // with, so the field is resolvable even though the writer never wrote it.
        const string writer = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"}]}""";
        const string reader = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"},{"name":"b","type":["null","int"],"default":null}]}""";

        var result = Check(reader, writer);
        Assert.That(result.IsCompatible, Is.True);
    }

    [Test]
    public static void Check_GivenReaderFieldAddedWithNullableTypeButNoDefault_ReportsMissingDefault()
    {
        // A nullable type is not itself a default: with no "default" key there is nothing to
        // populate the field with, which is what distinguishes this from an explicit null default.
        const string writer = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"}]}""";
        const string reader = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"},{"name":"b","type":["null","int"]}]}""";

        var result = Check(reader, writer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsCompatible, Is.False);
            Assert.That(result.Incompatibilities.Single().Type, Is.EqualTo(SchemaIncompatibilityType.ReaderFieldMissingDefaultValue));
        }
    }

    [Test]
    public static void Check_GivenReaderLogicalTypeOverTheWritersBaseType_IsCompatible()
    {
        // Logical types are annotations on a representation: resolution happens on the underlying
        // type, so annotating one side changes nothing about whether the data can be read.
        const string writer = "\"long\"";
        const string reader = """{"type":"long","logicalType":"timestamp-millis"}""";

        var result = Check(reader, writer);
        Assert.That(result.IsCompatible, Is.True);
    }

    [Test]
    public static void Check_GivenDecimalPrecisionAndScaleChanged_IsCompatible()
    {
        // Both sides are bytes on the wire. The reinterpretation may well lose value, but it is
        // not something schema resolution rejects.
        const string writer = """{"type":"bytes","logicalType":"decimal","precision":9,"scale":2}""";
        const string reader = """{"type":"bytes","logicalType":"decimal","precision":4,"scale":4}""";

        var result = Check(reader, writer);
        Assert.That(result.IsCompatible, Is.True);
    }

    [Test]
    public static void Check_GivenLogicalTypesOverIncompatibleBaseTypes_ReportsTheBaseTypeMismatch()
    {
        const string writer = "\"string\"";
        const string reader = """{"type":"int","logicalType":"date"}""";

        var result = Check(reader, writer);
        var incompatibility = result.Incompatibilities.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(incompatibility.Type, Is.EqualTo(SchemaIncompatibilityType.TypeMismatch));
            Assert.That(incompatibility.Message, Is.EqualTo("reader type: int not compatible with writer type: string"));
        }
    }

    [Test]
    public static void Check_GivenReaderUnionWithABranchReadingTheNonUnionWriter_IsCompatible()
    {
        var result = Check("""["null","int"]""", "\"int\"");
        Assert.That(result.IsCompatible, Is.True);
    }

    [Test]
    public static void Check_GivenReaderUnionWithNoBranchReadingTheNonUnionWriter_ReportsMissingUnionBranch()
    {
        var result = Check("""["null","string"]""", "\"int\"");
        var incompatibility = result.Incompatibilities.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(incompatibility.Type, Is.EqualTo(SchemaIncompatibilityType.MissingUnionBranch));
            Assert.That(incompatibility.Message, Is.EqualTo("reader union lacking writer type: int"));
        }
    }

    [Test]
    public static void Check_GivenMissingUnionBranchInsideAField_LocatesItAtTheFieldsType()
    {
        const string writer = """{"type":"record","name":"R","fields":[{"name":"a","type":["null","string"]}]}""";
        const string reader = """{"type":"record","name":"R","fields":[{"name":"a","type":["null","int"]}]}""";

        var result = Check(reader, writer);
        var incompatibility = result.Incompatibilities.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(incompatibility.Type, Is.EqualTo(SchemaIncompatibilityType.MissingUnionBranch));
            Assert.That(incompatibility.Location, Is.EqualTo("/fields/a/type"));
        }
    }

    [Test]
    public static void Check_GivenMissingEnumSymbolsInsideAField_LocatesThemAtTheEnumsSymbols()
    {
        const string writer = """{"type":"record","name":"R","fields":[{"name":"e","type":{"type":"enum","name":"E","symbols":["A","B"]}}]}""";
        const string reader = """{"type":"record","name":"R","fields":[{"name":"e","type":{"type":"enum","name":"E","symbols":["A"]}}]}""";

        var result = Check(reader, writer);
        var incompatibility = result.Incompatibilities.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(incompatibility.Type, Is.EqualTo(SchemaIncompatibilityType.MissingEnumSymbols));
            Assert.That(incompatibility.Location, Is.EqualTo("/fields/e/type/symbols"));
            Assert.That(incompatibility.Message, Is.EqualTo("[B]"));
        }
    }

    [Test]
    public static void Check_GivenFixedSizeMismatchInsideAField_LocatesItAtTheFixedsSize()
    {
        const string writer = """{"type":"record","name":"R","fields":[{"name":"f","type":{"type":"fixed","name":"F","size":8}}]}""";
        const string reader = """{"type":"record","name":"R","fields":[{"name":"f","type":{"type":"fixed","name":"F","size":16}}]}""";

        var result = Check(reader, writer);

        Assert.That(result.Incompatibilities.Single().Location, Is.EqualTo("/fields/f/type/size"));
    }

    [Test]
    public static void Check_GivenMapValueMismatch_ReportsTypeMismatchAtValues()
    {
        var result = Check("""{"type":"map","values":"int"}""", """{"type":"map","values":"string"}""");
        var incompatibility = result.Incompatibilities.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(incompatibility.Type, Is.EqualTo(SchemaIncompatibilityType.TypeMismatch));
            Assert.That(incompatibility.Location, Is.EqualTo("/values"));
        }
    }

    [Test]
    public static void Check_GivenRecursiveSchemaWithAnIncompatibility_TerminatesAndReportsIt()
    {
        const string writer = """
        {
            "type": "record",
            "name": "Node",
            "fields": [
                { "name": "value", "type": "string" },
                { "name": "next", "type": ["null", "Node"], "default": null }
            ]
        }
        """;
        const string reader = """
        {
            "type": "record",
            "name": "Node",
            "fields": [
                { "name": "value", "type": "int" },
                { "name": "next", "type": ["null", "Node"], "default": null }
            ]
        }
        """;

        var result = Check(reader, writer);
        var incompatibility = result.Incompatibilities.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(incompatibility.Type, Is.EqualTo(SchemaIncompatibilityType.TypeMismatch));
            Assert.That(incompatibility.Location, Is.EqualTo("/fields/value/type"));
        }
    }
}
