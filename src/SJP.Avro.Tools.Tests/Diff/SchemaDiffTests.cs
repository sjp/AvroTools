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

    [TestCase("int", "long", true)]
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
    public static void Compare_GivenUnionBranchAdded_ReportsUnionBranchAdded()
    {
        const string before = """{"type":"record","name":"R","fields":[{"name":"a","type":["null","string"]}]}""";
        const string after = """{"type":"record","name":"R","fields":[{"name":"a","type":["null","string","int"]}]}""";

        var result = Compare(before, after);
        var change = result.Changes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Kind, Is.EqualTo(ChangeKind.UnionBranchAdded));
            Assert.That(change.Message, Does.Contain("INT"));
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
            Assert.That(change.IsValidPromotion, Is.True);
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
}
