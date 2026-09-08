using System.Linq;
using Avro;
using NUnit.Framework;
using SJP.Avro.Tools.Diff;

namespace SJP.Avro.Tools.Tests.Diff;

[TestFixture]
internal static class SchemaDiffTests
{
    private static SchemaDiffResult Compare(string beforeJson, string afterJson, bool verbose = false)
    {
        var before = Schema.Parse(beforeJson);
        var after = Schema.Parse(afterJson);
        return SchemaDiff.Compare(before, after, verbose);
    }

    [Test]
    public static void Compare_GivenIdenticalSchemas_IsIdentical()
    {
        const string schema = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"}]}""";

        var result = Compare(schema, schema);

        Assert.That(result.IsIdentical, Is.True);
    }

    [Test]
    public static void Compare_GivenFieldOrderOnlyDifference_IsIdentical()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"},{"name":"b","type":"string"}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"b","type":"string"},{"name":"a","type":"int"}]}""";

        var result = Compare(before, after);

        Assert.That(result.IsIdentical, Is.True);
    }

    [Test]
    public static void Compare_GivenWhitespaceOnlyDifference_IsIdentical()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"}]}""";
        const string after = """
        {
            "type": "record",
            "name": "R",
            "fields": [
                { "name": "a", "type": "int" }
            ]
        }
        """;

        var result = Compare(before, after);

        Assert.That(result.IsIdentical, Is.True);
    }

    [Test]
    public static void Compare_GivenFieldAddedWithDefault_ReportsFieldAdded()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"},{"name":"b","type":"string","default":"x"}]}""";

        var result = Compare(before, after);
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.FieldAdded));
            Assert.That(change.Location, Is.EqualTo("/fields/b"));
            Assert.That(change.Message, Does.Contain("with a default"));
        }
    }

    [Test]
    public static void Compare_GivenFieldAddedWithoutDefault_ReportsFieldAdded()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"},{"name":"b","type":"string"}]}""";

        var result = Compare(before, after);
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.FieldAdded));
            Assert.That(change.Message, Does.Contain("without a default"));
        }
    }

    [Test]
    public static void Compare_GivenFieldRemoved_ReportsFieldRemoved()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"},{"name":"b","type":"string"}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"}]}""";

        var result = Compare(before, after);
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.FieldRemoved));
            Assert.That(change.Location, Is.EqualTo("/fields/b"));
        }
    }

    // A promotion is valid when a reader using the new type can read data written with the old
    // one, so it is directional: widening is valid, narrowing is not.
    [TestCase("int", "long", true)]
    [TestCase("int", "double", true)]
    [TestCase("float", "double", true)]
    [TestCase("long", "int", false)]
    [TestCase("double", "float", false)]
    [TestCase("string", "bytes", true)]
    [TestCase("bytes", "string", true)]
    [TestCase("int", "string", false)]
    public static void Compare_GivenFieldTypeChanged_ReportsFieldTypeChanged(string beforeType, string afterType, bool expectedPromotion)
    {
        var before = $$"""{"type":"record","name":"R","fields":[{"name":"a","type":"{{beforeType}}"}]}""";
        var after = $$"""{"type":"record","name":"R","fields":[{"name":"a","type":"{{afterType}}"}]}""";

        var result = Compare(before, after);
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.FieldTypeChanged));
            Assert.That(change.Location, Is.EqualTo("/fields/a/type"));
            Assert.That(change.Message, Is.EqualTo($"type changed from {beforeType} to {afterType}"));
            Assert.That(change.OldValue, Is.EqualTo(beforeType));
            Assert.That(change.NewValue, Is.EqualTo(afterType));
            Assert.That(change.IsValidPromotion, Is.EqualTo(expectedPromotion));
        }
    }

    [Test]
    public static void Compare_GivenFieldDefaultAdded_ReportsFieldDefaultAdded()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"a","type":"int","default":0}]}""";

        var result = Compare(before, after);

        Assert.That(result.Changes.Single().Kind, Is.EqualTo(ChangeKind.FieldDefaultAdded));
    }

    [Test]
    public static void Compare_GivenFieldDefaultRemoved_ReportsFieldDefaultRemoved()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"a","type":"int","default":0}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"}]}""";

        var result = Compare(before, after);

        Assert.That(result.Changes.Single().Kind, Is.EqualTo(ChangeKind.FieldDefaultRemoved));
    }

    [Test]
    public static void Compare_GivenFieldDefaultChanged_ReportsFieldDefaultChanged()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"a","type":"int","default":0}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"a","type":"int","default":1}]}""";

        var result = Compare(before, after);

        Assert.That(result.Changes.Single().Kind, Is.EqualTo(ChangeKind.FieldDefaultChanged));
    }

    [Test]
    public static void Compare_GivenDefaultReorderedButStructurallyEqual_IsIdentical()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"a","type":{"type":"map","values":"int"},"default":{"x":1,"y":2}}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"a","type":{"type":"map","values":"int"},"default":{"y":2,"x":1}}]}""";

        var result = Compare(before, after);

        Assert.That(result.IsIdentical, Is.True);
    }

    [Test]
    public static void Compare_GivenFieldRenamedWithAlias_ReportsFieldRenamed()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"b","type":"int","aliases":["a"]}]}""";

        var result = Compare(before, after);
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.FieldRenamed));
            Assert.That(change.OldValue, Is.EqualTo("a"));
            Assert.That(change.NewValue, Is.EqualTo("b"));
        }
    }

    [Test]
    public static void Compare_GivenFieldRenamedWithoutAlias_ReportsAddAndRemove()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"b","type":"int"}]}""";

        var result = Compare(before, after);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Changes, Has.Count.EqualTo(2));
            Assert.That(result.Changes, Has.Some.Matches<SchemaChange>(c => c.Kind == ChangeKind.FieldAdded && c.Location == "/fields/b"));
            Assert.That(result.Changes, Has.Some.Matches<SchemaChange>(c => c.Kind == ChangeKind.FieldRemoved && c.Location == "/fields/a"));
        }
    }

    [Test]
    public static void Compare_GivenEnumSymbolAdded_ReportsEnumSymbolAdded()
    {
        const string before = """{"type":"enum","name":"E","symbols":["A","B"]}""";
        const string after = """{"type":"enum","name":"E","symbols":["A","B","C"]}""";

        var result = Compare(before, after);
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.EnumSymbolAdded));
            Assert.That(change.Message, Does.Contain("C"));
        }
    }

    [Test]
    public static void Compare_GivenEnumSymbolRemoved_ReportsEnumSymbolRemoved()
    {
        const string before = """{"type":"enum","name":"E","symbols":["A","B","C"]}""";
        const string after = """{"type":"enum","name":"E","symbols":["A","B"]}""";

        var result = Compare(before, after);

        Assert.That(result.Changes.Single().Kind, Is.EqualTo(ChangeKind.EnumSymbolRemoved));
    }

    [Test]
    public static void Compare_GivenEnumSymbolsReordered_ReportsReordered()
    {
        const string before = """{"type":"enum","name":"E","symbols":["A","B","C"]}""";
        const string after = """{"type":"enum","name":"E","symbols":["C","B","A"]}""";

        var result = Compare(before, after);

        Assert.That(result.Changes.Single().Kind, Is.EqualTo(ChangeKind.EnumSymbolsReordered));
    }

    [Test]
    public static void Compare_GivenEnumSymbolsSameOrder_IsIdentical()
    {
        const string schema = """{"type":"enum","name":"E","symbols":["A","B","C"]}""";

        var result = Compare(schema, schema);

        Assert.That(result.IsIdentical, Is.True);
    }

    [Test]
    public static void Compare_GivenFixedSizeChanged_ReportsFixedSizeChanged()
    {
        const string before = """{"type":"fixed","name":"F","size":16}""";
        const string after = """{"type":"fixed","name":"F","size":32}""";

        var result = Compare(before, after);
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.FixedSizeChanged));
            Assert.That(change.OldValue, Is.EqualTo("16"));
            Assert.That(change.NewValue, Is.EqualTo("32"));
        }
    }

    [Test]
    public static void Compare_GivenLogicalTypeAdded_ReportsLogicalTypeChanged()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"age","type":"int"}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"age","type":{"type":"int","logicalType":"date"}}]}""";

        var result = Compare(before, after);
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.LogicalTypeChanged));
            Assert.That(change.Location, Is.EqualTo("/fields/age/type/logicalType"));
            Assert.That(change.OldValue, Is.Null);
            Assert.That(change.NewValue, Is.EqualTo("date"));
        }
    }

    [Test]
    public static void Compare_GivenLogicalTypeRemoved_ReportsLogicalTypeChanged()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"age","type":{"type":"int","logicalType":"date"}}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"age","type":"int"}]}""";

        var result = Compare(before, after);
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.LogicalTypeChanged));
            Assert.That(change.Location, Is.EqualTo("/fields/age/type/logicalType"));
            Assert.That(change.OldValue, Is.EqualTo("date"));
            Assert.That(change.NewValue, Is.Null);
        }
    }

    [Test]
    public static void Compare_GivenLogicalTypeReplaced_ReportsLogicalTypeChanged()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"at","type":{"type":"long","logicalType":"timestamp-millis"}}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"at","type":{"type":"long","logicalType":"timestamp-micros"}}]}""";

        var result = Compare(before, after);
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.LogicalTypeChanged));
            Assert.That(change.OldValue, Is.EqualTo("timestamp-millis"));
            Assert.That(change.NewValue, Is.EqualTo("timestamp-micros"));
        }
    }

    [Test]
    public static void Compare_GivenIdenticalLogicalTypes_IsIdentical()
    {
        const string schema = """{"type":"record","name":"R","fields":[{"name":"id","type":{"type":"string","logicalType":"uuid"}}]}""";

        var result = Compare(schema, schema);

        Assert.That(result.IsIdentical, Is.True);
    }

    [Test]
    public static void Compare_GivenTopLevelLogicalTypeAdded_ReportsLogicalTypeChanged()
    {
        var result = Compare("\"int\"", """{"type":"int","logicalType":"date"}""");
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.LogicalTypeChanged));
            Assert.That(change.Location, Is.EqualTo("/logicalType"));
        }
    }

    [Test]
    public static void Compare_GivenDecimalPrecisionChanged_ReportsLogicalTypeAttributeChanged()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"amount","type":{"type":"bytes","logicalType":"decimal","precision":9,"scale":2}}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"amount","type":{"type":"bytes","logicalType":"decimal","precision":12,"scale":2}}]}""";

        var result = Compare(before, after);
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.LogicalTypeAttributeChanged));
            Assert.That(change.Location, Is.EqualTo("/fields/amount/type/precision"));
            Assert.That(change.OldValue, Is.EqualTo("9"));
            Assert.That(change.NewValue, Is.EqualTo("12"));
        }
    }

    [Test]
    public static void Compare_GivenDecimalScaleChanged_ReportsLogicalTypeAttributeChanged()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"amount","type":{"type":"bytes","logicalType":"decimal","precision":9,"scale":2}}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"amount","type":{"type":"bytes","logicalType":"decimal","precision":9,"scale":4}}]}""";

        var result = Compare(before, after);
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.LogicalTypeAttributeChanged));
            Assert.That(change.Location, Is.EqualTo("/fields/amount/type/scale"));
            Assert.That(change.OldValue, Is.EqualTo("2"));
            Assert.That(change.NewValue, Is.EqualTo("4"));
        }
    }

    [Test]
    public static void Compare_GivenOmittedAndExplicitZeroDecimalScale_IsIdentical()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"amount","type":{"type":"bytes","logicalType":"decimal","precision":9}}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"amount","type":{"type":"bytes","logicalType":"decimal","precision":9,"scale":0}}]}""";

        var result = Compare(before, after);

        Assert.That(result.IsIdentical, Is.True);
    }

    [Test]
    public static void Compare_GivenBaseTypeAndLogicalTypeBothChanged_ReportsBoth()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"id","type":"int"}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"id","type":{"type":"string","logicalType":"uuid"}}]}""";

        var result = Compare(before, after);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Changes.Select(c => c.Kind), Is.EqualTo(new[]
            {
                ChangeKind.FieldTypeChanged,
                ChangeKind.LogicalTypeChanged,
            }));
            Assert.That(result.Changes.Select(c => c.Location), Is.EqualTo(new[]
            {
                "/fields/id/type",
                "/fields/id/type/logicalType",
            }));
        }
    }

    [Test]
    public static void Compare_GivenUnionBranchGainingLogicalType_ReportsLogicalTypeChanged()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"a","type":["null","int"]}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"a","type":["null",{"type":"int","logicalType":"date"}]}]}""";

        var result = Compare(before, after);
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.LogicalTypeChanged));
            Assert.That(change.NewValue, Is.EqualTo("date"));
        }
    }

    [Test]
    public static void Compare_GivenSameBaseTypeReachedWithAndWithoutLogicalType_ReportsOnlyTheAnnotatedPosition()
    {
        const string before = """
        {
            "type": "record",
            "name": "R",
            "fields": [
                { "name": "plain", "type": "int" },
                { "name": "annotated", "type": "int" }
            ]
        }
        """;
        const string after = """
        {
            "type": "record",
            "name": "R",
            "fields": [
                { "name": "plain", "type": "int" },
                { "name": "annotated", "type": { "type": "int", "logicalType": "date" } }
            ]
        }
        """;

        var result = Compare(before, after);

        Assert.That(result.Changes.Select(c => c.Location), Is.EqualTo(new[] { "/fields/annotated/type/logicalType" }));
    }

    [Test]
    public static void Compare_GivenUnionBranchAdded_ReportsUnionBranchAdded()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"a","type":["null","string"]}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"a","type":["null","string","int"]}]}""";

        var result = Compare(before, after);
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.UnionBranchAdded));
            Assert.That(change.Message, Does.Contain("int"));
        }
    }

    [Test]
    public static void Compare_GivenUnionBranchRemoved_ReportsUnionBranchRemoved()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"a","type":["null","string","int"]}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"a","type":["null","string"]}]}""";

        var result = Compare(before, after);

        Assert.That(result.Changes.Single().Kind, Is.EqualTo(ChangeKind.UnionBranchRemoved));
    }

    [Test]
    public static void Compare_GivenUnionBranchesReordered_ReportsUnionBranchesReordered()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"a","type":["null","string"]}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"a","type":["string","null"]}]}""";

        var result = Compare(before, after);
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.UnionBranchesReordered));
            Assert.That(change.Location, Is.EqualTo("/fields/a/type"));
            Assert.That(change.OldValue, Is.EqualTo("null, string"));
            Assert.That(change.NewValue, Is.EqualTo("string, null"));
        }
    }

    [Test]
    public static void Compare_GivenUnionBranchesReorderedAndBranchAdded_ReportsBoth()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"a","type":["null","string"]}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"a","type":["string","int","null"]}]}""";

        var result = Compare(before, after);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Changes.Select(c => c.Kind), Is.EquivalentTo(new[]
            {
                ChangeKind.UnionBranchAdded,
                ChangeKind.UnionBranchesReordered,
            }));
            Assert.That(
                result.Changes.Single(c => c.Kind == ChangeKind.UnionBranchesReordered).NewValue,
                Is.EqualTo("string, int, null"));
        }
    }

    [Test]
    public static void Compare_GivenUnionBranchAddedWithoutReordering_ReportsOnlyTheAddition()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"a","type":["null","string"]}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"a","type":["int","null","string"]}]}""";

        var result = Compare(before, after);

        Assert.That(result.Changes.Single().Kind, Is.EqualTo(ChangeKind.UnionBranchAdded));
    }

    [Test]
    public static void Compare_GivenNamedUnionBranchesReordered_DescribesBranchesByName()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"a","type":[{"type":"enum","name":"E","symbols":["A"]},{"type":"fixed","name":"F","size":4}]}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"a","type":[{"type":"fixed","name":"F","size":4},{"type":"enum","name":"E","symbols":["A"]}]}]}""";

        var result = Compare(before, after);
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.UnionBranchesReordered));
            Assert.That(change.OldValue, Is.EqualTo("E, F"));
            Assert.That(change.NewValue, Is.EqualTo("F, E"));
        }
    }

    [Test]
    public static void Compare_GivenChangeInsideNamedUnionBranch_LocatesItByBranchName()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"a","type":["null",{"type":"record","name":"Inner","fields":[{"name":"b","type":"int"}]}]}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"a","type":["null",{"type":"record","name":"Inner","fields":[{"name":"b","type":"string"}]}]}]}""";

        var result = Compare(before, after);
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.FieldTypeChanged));
            Assert.That(change.Location, Is.EqualTo("/fields/a/type/Inner/fields/b/type"));
        }
    }

    [Test]
    public static void Compare_GivenChangeInsidePrimitiveUnionBranch_LocatesItByAvroTypeName()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"a","type":["null","int"]}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"a","type":["null",{"type":"int","logicalType":"date"}]}]}""";

        var result = Compare(before, after);
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.LogicalTypeChanged));
            Assert.That(change.Location, Is.EqualTo("/fields/a/type/int/logicalType"));
        }
    }

    [Test]
    public static void Compare_GivenUnionBranchRenamedThroughAlias_ReportsRenameAndComparesBranch()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"a","type":["null",{"type":"record","name":"Addr","fields":[{"name":"street","type":"string"}]}]}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"a","type":["null",{"type":"record","name":"Address","aliases":["Addr"],"fields":[{"name":"street","type":"int"}]}]}]}""";

        var result = Compare(before, after);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Changes.Select(c => c.Kind), Is.EqualTo(new[]
            {
                ChangeKind.TypeRenamed,
                ChangeKind.FieldTypeChanged,
            }));
            Assert.That(result.Changes.Select(c => c.Location), Is.EqualTo(new[]
            {
                "/fields/a/type/Address",
                "/fields/a/type/Address/fields/street/type",
            }));
        }
    }

    [Test]
    public static void Compare_GivenUnionBranchRenamedThroughAlias_DoesNotReportAddedOrRemovedBranches()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"a","type":[{"type":"enum","name":"Old","symbols":["A"]},"null"]}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"a","type":[{"type":"enum","name":"New","aliases":["Old"],"symbols":["A"]},"null"]}]}""";

        var result = Compare(before, after);
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.TypeRenamed));
            Assert.That(change.OldValue, Is.EqualTo("Old"));
            Assert.That(change.NewValue, Is.EqualTo("New"));
        }
    }

    [Test]
    public static void Compare_GivenDocChanged_OnlyReportedWhenVerbose()
    {
        const string before = """{"type":"record","name":"R","doc":"old","fields":[{"name":"a","type":"int"}]}""";
        const string after = """{"type":"record","name":"R","doc":"new","fields":[{"name":"a","type":"int"}]}""";

        var quiet = Compare(before, after, verbose: false);
        var verbose = Compare(before, after, verbose: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(quiet.IsIdentical, Is.True);
            Assert.That(verbose.Changes.Single().Kind, Is.EqualTo(ChangeKind.MetadataChanged));
        }
    }

    [Test]
    public static void Compare_GivenTopLevelTypeKindChanged_ReportsTypeKindChanged()
    {
        var result = Compare("\"int\"", "\"long\"");
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.TypeKindChanged));
            Assert.That(change.Location, Is.EqualTo("/"));
            Assert.That(change.OldValue, Is.EqualTo("int"));
            Assert.That(change.NewValue, Is.EqualTo("long"));
            Assert.That(change.IsValidPromotion, Is.True);
        }
    }

    [Test]
    public static void Compare_GivenTopLevelTypeNarrowed_ReportsNotAValidPromotion()
    {
        var result = Compare("\"long\"", "\"int\"");
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.TypeKindChanged));
            Assert.That(change.IsValidPromotion, Is.False);
        }
    }

    [Test]
    public static void Compare_GivenRootRecordRenamedWithAlias_ReportsTypeRenamed()
    {
        const string before = """{"type":"record","name":"Old","fields":[{"name":"a","type":"int"}]}""";
        const string after = """{"type":"record","name":"New","aliases":["Old"],"fields":[{"name":"a","type":"int"}]}""";

        var result = Compare(before, after);
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.TypeRenamed));
            Assert.That(change.Location, Is.EqualTo("/"));
            Assert.That(change.OldValue, Is.EqualTo("Old"));
            Assert.That(change.NewValue, Is.EqualTo("New"));
        }
    }

    [Test]
    public static void Compare_GivenRootRecordMovedToAnotherNamespaceWithAlias_ReportsTypeRenamed()
    {
        const string before = """{"type":"record","name":"R","namespace":"v1","fields":[{"name":"a","type":"int"}]}""";
        const string after = """{"type":"record","name":"R","namespace":"v2","aliases":["v1.R"],"fields":[{"name":"a","type":"int"}]}""";

        var result = Compare(before, after);
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.TypeRenamed));
            Assert.That(change.OldValue, Is.EqualTo("v1.R"));
            Assert.That(change.NewValue, Is.EqualTo("v2.R"));
        }
    }

    [Test]
    public static void Compare_GivenRootRecordRenamedWithBareAliasInSameNamespace_ReportsTypeRenamed()
    {
        const string before = """{"type":"record","name":"Old","namespace":"ns","fields":[{"name":"a","type":"int"}]}""";
        const string after = """{"type":"record","name":"New","namespace":"ns","aliases":["Old"],"fields":[{"name":"a","type":"int"}]}""";

        var result = Compare(before, after);
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.TypeRenamed));
            Assert.That(change.OldValue, Is.EqualTo("ns.Old"));
            Assert.That(change.NewValue, Is.EqualTo("ns.New"));
        }
    }

    [Test]
    public static void Compare_GivenRootRecordRenamedWithBareAliasInAnotherNamespace_DoesNotReportTypeRenamed()
    {
        const string before = """{"type":"record","name":"Old","namespace":"other","fields":[{"name":"a","type":"int"}]}""";
        const string after = """{"type":"record","name":"New","namespace":"ns","aliases":["Old"],"fields":[{"name":"a","type":"int"}]}""";

        var result = Compare(before, after);

        Assert.That(result.Changes.Any(c => c.Kind == ChangeKind.TypeRenamed), Is.False);
    }

    [Test]
    public static void Compare_GivenNestedRecordRenamedWithAlias_ReportsTypeRenamedAtNestedLocation()
    {
        const string before = """
        {
            "type": "record",
            "name": "Person",
            "fields": [
                { "name": "home", "type": { "type": "record", "name": "Addr", "fields": [{ "name": "street", "type": "string" }] } }
            ]
        }
        """;
        const string after = """
        {
            "type": "record",
            "name": "Person",
            "fields": [
                {
                    "name": "home",
                    "type": {
                        "type": "record",
                        "name": "Address",
                        "aliases": ["Addr"],
                        "fields": [{ "name": "street", "type": "string" }]
                    }
                }
            ]
        }
        """;

        var result = Compare(before, after);
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.TypeRenamed));
            Assert.That(change.Location, Is.EqualTo("/fields/home/type"));
            Assert.That(change.OldValue, Is.EqualTo("Addr"));
            Assert.That(change.NewValue, Is.EqualTo("Address"));
        }
    }

    [Test]
    public static void Compare_GivenRenamedRecordWithFieldChange_ReportsBothTypeRenamedAndFieldChange()
    {
        const string before = """{"type":"record","name":"Old","fields":[{"name":"a","type":"int"}]}""";
        const string after = """{"type":"record","name":"New","aliases":["Old"],"fields":[{"name":"a","type":"int"},{"name":"b","type":"string","default":""}]}""";

        var result = Compare(before, after);

        Assert.That(result.Changes.Select(c => c.Kind), Is.EqualTo(new[]
        {
            ChangeKind.TypeRenamed,
            ChangeKind.FieldAdded,
        }));
    }

    [Test]
    public static void Compare_GivenEnumRenamedWithAlias_ReportsTypeRenamed()
    {
        const string before = """{"type":"enum","name":"Old","symbols":["A","B"]}""";
        const string after = """{"type":"enum","name":"New","aliases":["Old"],"symbols":["A","B"]}""";

        var result = Compare(before, after);
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.TypeRenamed));
            Assert.That(change.OldValue, Is.EqualTo("Old"));
            Assert.That(change.NewValue, Is.EqualTo("New"));
        }
    }

    [Test]
    public static void Compare_GivenRecordRenamedWithoutAlias_ReportsTypeKindChanged()
    {
        const string before = """{"type":"record","name":"Old","fields":[{"name":"a","type":"int"}]}""";
        const string after = """{"type":"record","name":"New","fields":[{"name":"a","type":"int"}]}""";

        var result = Compare(before, after);

        Assert.That(result.Changes.Single().Kind, Is.EqualTo(ChangeKind.TypeKindChanged));
    }

    [Test]
    public static void Compare_GivenNamedTypeAliasesChanged_ReportsMetadataChangedOnlyWhenVerbose()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"}]}""";
        const string after = """{"type":"record","name":"R","aliases":["Legacy"],"fields":[{"name":"a","type":"int"}]}""";

        var quiet = Compare(before, after, verbose: false);
        var verbose = Compare(before, after, verbose: true);
        var change = verbose.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(quiet.IsIdentical, Is.True);
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.MetadataChanged));
            Assert.That(change.Location, Is.EqualTo("/aliases"));
            Assert.That(change.NewValue, Is.EqualTo("Legacy"));
        }
    }

    [Test]
    public static void Compare_GivenRecursiveSchema_TerminatesAndReportsRealChange()
    {
        const string before = """
        {
            "type": "record",
            "name": "Tree",
            "fields": [
                { "name": "children", "type": { "type": "array", "items": "Tree" } }
            ]
        }
        """;
        const string after = """
        {
            "type": "record",
            "name": "Tree",
            "fields": [
                { "name": "children", "type": { "type": "array", "items": "Tree" } },
                { "name": "label", "type": "string" }
            ]
        }
        """;

        var result = Compare(before, after);

        Assert.That(result.Changes.Single().Kind, Is.EqualTo(ChangeKind.FieldAdded));
    }

    [Test]
    public static void Compare_GivenTypeReachedThroughRecursion_ReportsChangeAtEachLocation()
    {
        // "X" is first reached inside "B", where the walk stops at the recursion back into "B" and
        // so cannot see the change below it. Reached again as Root's own field, where "B" is not
        // being walked, everything under it must still be reported.
        const string before = """
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
        const string after = """
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

        var result = Compare(before, after);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Changes.Select(c => c.Kind), Is.All.EqualTo(ChangeKind.FieldTypeChanged));
            Assert.That(result.Changes.Select(c => c.Location), Is.EquivalentTo(new[]
            {
                "/fields/b/type/fields/d/type",
                "/fields/x/type/fields/r/type/fields/d/type",
            }));
        }
    }

    [Test]
    public static void Compare_GivenTypeUsedByTwoFields_ReportsChangeAtEachFieldsLocation()
    {
        const string before = """
        {
            "type": "record",
            "name": "Person",
            "fields": [
                { "name": "home", "type": { "type": "record", "name": "Address", "fields": [{ "name": "street", "type": "string" }] } },
                { "name": "work", "type": "Address" }
            ]
        }
        """;
        const string after = """
        {
            "type": "record",
            "name": "Person",
            "fields": [
                {
                    "name": "home",
                    "type": {
                        "type": "record",
                        "name": "Address",
                        "fields": [{ "name": "street", "type": "string" }, { "name": "zip", "type": "string", "default": "" }]
                    }
                },
                { "name": "work", "type": "Address" }
            ]
        }
        """;

        var result = Compare(before, after);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Changes.Select(c => c.Kind), Is.All.EqualTo(ChangeKind.FieldAdded));
            Assert.That(result.Changes.Select(c => c.Location), Is.EquivalentTo(new[]
            {
                "/fields/home/type/fields/zip",
                "/fields/work/type/fields/zip",
            }));
        }
    }

    [Test]
    public static void Compare_GivenTypeUsedByAFieldAndAnArray_ReportsChangeAtEachLocation()
    {
        const string before = """
        {
            "type": "record",
            "name": "Person",
            "fields": [
                { "name": "home", "type": { "type": "record", "name": "Address", "fields": [{ "name": "street", "type": "string" }] } },
                { "name": "previous", "type": { "type": "array", "items": "Address" } }
            ]
        }
        """;
        const string after = """
        {
            "type": "record",
            "name": "Person",
            "fields": [
                {
                    "name": "home",
                    "type": {
                        "type": "record",
                        "name": "Address",
                        "fields": [{ "name": "street", "type": "string" }, { "name": "zip", "type": "string", "default": "" }]
                    }
                },
                { "name": "previous", "type": { "type": "array", "items": "Address" } }
            ]
        }
        """;

        var result = Compare(before, after);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Changes.Select(c => c.Kind), Is.All.EqualTo(ChangeKind.FieldAdded));
            Assert.That(result.Changes.Select(c => c.Location), Is.EquivalentTo(new[]
            {
                "/fields/home/type/fields/zip",
                "/fields/previous/type/items/fields/zip",
            }));
        }
    }

    [Test]
    public static void Compare_GivenSharedTypeReplacedInTwoFields_ReportsFieldTypeChangedForBoth()
    {
        const string before = """
        {
            "type": "record",
            "name": "Person",
            "fields": [
                { "name": "home", "type": { "type": "record", "name": "Address", "fields": [{ "name": "street", "type": "string" }] } },
                { "name": "work", "type": "Address" }
            ]
        }
        """;
        const string after = """
        {
            "type": "record",
            "name": "Person",
            "fields": [
                { "name": "home", "type": { "type": "record", "name": "Place", "fields": [{ "name": "street", "type": "string" }] } },
                { "name": "work", "type": "Place" }
            ]
        }
        """;

        var result = Compare(before, after);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Changes.Select(c => c.Kind), Is.All.EqualTo(ChangeKind.FieldTypeChanged));
            Assert.That(result.Changes.Select(c => c.Location), Is.EquivalentTo(new[]
            {
                "/fields/home/type",
                "/fields/work/type",
            }));
        }
    }

    [Test]
    public static void Compare_GivenRecordBecameErrorOfSameShape_ReportsMetadataChangedOnlyWhenVerbose()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"}]}""";
        const string after = """{"type":"error","name":"R","fields":[{"name":"a","type":"int"}]}""";

        var quiet = Compare(before, after);
        var verbose = Compare(before, after, verbose: true);
        var change = verbose.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(quiet.IsIdentical, Is.True);
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.MetadataChanged));
            Assert.That(change.Location, Is.EqualTo("/type"));
            Assert.That(change.OldValue, Is.EqualTo("record"));
            Assert.That(change.NewValue, Is.EqualTo("error"));
        }
    }

    [Test]
    public static void Compare_GivenErrorBecameRecordWithAddedField_ReportsTheFieldNotTheTypeKind()
    {
        const string before = """{"type":"error","name":"R","fields":[{"name":"a","type":"int"}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"},{"name":"b","type":"int"}]}""";

        var result = Compare(before, after);
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.FieldAdded));
            Assert.That(change.Location, Is.EqualTo("/fields/b"));
        }
    }
}
