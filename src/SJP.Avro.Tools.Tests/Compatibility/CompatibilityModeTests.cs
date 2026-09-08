using System;
using System.Collections.Generic;
using System.Linq;
using Avro;
using NUnit.Framework;
using SJP.Avro.Tools.Compatibility;

namespace SJP.Avro.Tools.Tests.Compatibility;

[TestFixture]
internal static class CompatibilityModeTests
{
    // Dropping a field that has no default is readable by the newer schema (it ignores the extra
    // written field) but not by the older one (it has nothing to fill the field it still expects),
    // so this pair is backward compatible and forward incompatible.
    private const string WithField = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"},{"name":"b","type":"int"}]}""";
    private const string WithoutField = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"}]}""";

    // A chain where the candidate matches the most recent version but not the one before it.
    private const string IntField = """{"type":"record","name":"R","fields":[{"name":"a","type":"int"}]}""";
    private const string StringField = """{"type":"record","name":"R","fields":[{"name":"a","type":"string"}]}""";

    private static IReadOnlyList<Schema> Schemas(params string[] json) =>
        Array.ConvertAll(json, Schema.Parse);

    [Test]
    public static void Check_GivenUndefinedMode_ThrowsArgumentException()
    {
        Assert.That(() => SchemaCompatibility.Check((CompatibilityMode)42, Schemas(IntField, IntField)), Throws.ArgumentException);
    }

    [Test]
    public static void Check_GivenNullSchemas_ThrowsArgumentNullException()
    {
        Assert.That(() => SchemaCompatibility.Check(CompatibilityMode.Backward, null!), Throws.ArgumentNullException);
    }

    [Test]
    public static void Check_GivenNullSchemaInSet_ThrowsArgumentNullException()
    {
        var schemas = new[] { Schema.Parse(IntField), null! };
        Assert.That(() => SchemaCompatibility.Check(CompatibilityMode.Backward, schemas), Throws.ArgumentNullException);
    }

    [Test]
    public static void Check_GivenFewerThanTwoSchemas_ThrowsArgumentException()
    {
        Assert.That(() => SchemaCompatibility.Check(CompatibilityMode.Backward, Schemas(IntField)), Throws.ArgumentException);
    }

    [TestCase(CompatibilityMode.Backward)]
    [TestCase(CompatibilityMode.Forward)]
    [TestCase(CompatibilityMode.Full)]
    public static void Check_GivenNonTransitiveModeAndMoreThanTwoSchemas_ThrowsArgumentException(CompatibilityMode mode)
    {
        Assert.That(() => SchemaCompatibility.Check(mode, Schemas(IntField, IntField, IntField)), Throws.ArgumentException);
    }

    [TestCase(CompatibilityMode.Backward, false)]
    [TestCase(CompatibilityMode.Forward, false)]
    [TestCase(CompatibilityMode.Full, false)]
    [TestCase(CompatibilityMode.BackwardTransitive, true)]
    [TestCase(CompatibilityMode.ForwardTransitive, true)]
    [TestCase(CompatibilityMode.FullTransitive, true)]
    public static void IsTransitive_GivenMode_ClassifiesByWhetherEveryVersionIsChecked(CompatibilityMode mode, bool expected)
    {
        Assert.That(SchemaCompatibility.IsTransitive(mode), Is.EqualTo(expected));
    }

    [Test]
    public static void Check_GivenBackward_MakesTheCandidateTheReader()
    {
        var result = SchemaCompatibility.Check(CompatibilityMode.Backward, Schemas(WithoutField, WithField));
        var check = result.Checks.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(check.Direction, Is.EqualTo(CompatibilityDirection.Backward));
            Assert.That(check.ReaderIndex, Is.Zero);
            Assert.That(check.WriterIndex, Is.EqualTo(1));
            Assert.That(result.Mode, Is.EqualTo(CompatibilityMode.Backward));
            Assert.That(result.IsCompatible, Is.True);
        }
    }

    [Test]
    public static void Check_GivenForward_MakesTheCandidateTheWriter()
    {
        var result = SchemaCompatibility.Check(CompatibilityMode.Forward, Schemas(WithoutField, WithField));
        var check = result.Checks.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(check.Direction, Is.EqualTo(CompatibilityDirection.Forward));
            Assert.That(check.ReaderIndex, Is.EqualTo(1));
            Assert.That(check.WriterIndex, Is.Zero);
            Assert.That(result.IsCompatible, Is.False);
        }
    }

    [Test]
    public static void Check_GivenFull_ChecksBothDirectionsAndFailsIfEitherDoes()
    {
        var result = SchemaCompatibility.Check(CompatibilityMode.Full, Schemas(WithoutField, WithField));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Checks.Select(c => c.Direction), Is.EqualTo(new[] { CompatibilityDirection.Backward, CompatibilityDirection.Forward }));
            Assert.That(result.Checks[0].Result.IsCompatible, Is.True);
            Assert.That(result.Checks[1].Result.IsCompatible, Is.False);
            Assert.That(result.IsCompatible, Is.False);
        }
    }

    [Test]
    public static void Check_GivenSchemasThatMatchInBothDirections_IsCompatible()
    {
        var result = SchemaCompatibility.Check(CompatibilityMode.Full, Schemas(IntField, IntField));
        Assert.That(result.IsCompatible, Is.True);
    }

    [Test]
    public static void Check_GivenBackward_LooksOnlyAtTheMostRecentVersion()
    {
        var result = SchemaCompatibility.Check(CompatibilityMode.Backward, Schemas(IntField, IntField));
        Assert.That(result.IsCompatible, Is.True);
    }

    [Test]
    public static void Check_GivenBackwardTransitive_ChecksTheCandidateAgainstEveryVersion()
    {
        var result = SchemaCompatibility.Check(CompatibilityMode.BackwardTransitive, Schemas(IntField, IntField, StringField));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Checks.Select(c => c.Direction), Is.All.EqualTo(CompatibilityDirection.Backward));
            Assert.That(result.Checks.Select(c => c.ReaderIndex), Is.EqualTo(new[] { 0, 0 }));
            Assert.That(result.Checks.Select(c => c.WriterIndex), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(result.Checks[0].Result.IsCompatible, Is.True);
            Assert.That(result.Checks[1].Result.IsCompatible, Is.False);
            Assert.That(result.IsCompatible, Is.False);
        }
    }

    [Test]
    public static void Check_GivenForwardTransitive_MakesEveryVersionAReaderOfTheCandidate()
    {
        var result = SchemaCompatibility.Check(CompatibilityMode.ForwardTransitive, Schemas(IntField, IntField, StringField));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Checks.Select(c => c.Direction), Is.All.EqualTo(CompatibilityDirection.Forward));
            Assert.That(result.Checks.Select(c => c.ReaderIndex), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(result.Checks.Select(c => c.WriterIndex), Is.EqualTo(new[] { 0, 0 }));
            Assert.That(result.IsCompatible, Is.False);
        }
    }

    [Test]
    public static void Check_GivenFullTransitive_ChecksBothDirectionsAgainstEveryVersion()
    {
        var result = SchemaCompatibility.Check(CompatibilityMode.FullTransitive, Schemas(IntField, IntField, IntField));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Checks, Has.Count.EqualTo(4));
            Assert.That(result.Checks.Select(c => c.Direction), Is.EqualTo(new[]
            {
                CompatibilityDirection.Backward,
                CompatibilityDirection.Forward,
                CompatibilityDirection.Backward,
                CompatibilityDirection.Forward,
            }));
            Assert.That(result.IsCompatible, Is.True);
        }
    }

    [Test]
    public static void Check_GivenAnyMode_ReportsTheSchemasEachCheckCompared()
    {
        var schemas = Schemas(WithoutField, WithField);
        var check = SchemaCompatibility.Check(CompatibilityMode.Backward, schemas).Checks.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(check.Reader, Is.SameAs(schemas[0]));
            Assert.That(check.Writer, Is.SameAs(schemas[1]));
        }
    }
}
