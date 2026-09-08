using System;
using Avro;
using Avro.Specific;
using NUnit.Framework;

namespace SJP.Avro.Tools.CodeGen.Tests;

/// <summary>
/// Compiles generator output for each output-style option combination and drives it through
/// Avro's construct-then-<c>Put</c> deserialization pattern (a parameterless constructor followed
/// by positional <c>Put</c> calls), which is the concern that motivates --required/--init-only in
/// the first place: init-only accessors are not otherwise assignable from a plain instance method.
/// </summary>
[TestFixture]
internal static class AvroRecordGeneratorRoundTripTests
{
    private const string TestNamespace = "Test.Avro.RoundTrip";

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public static void Generate_GivenOptionCombination_ProducesTypeCompatibleWithConstructThenPut(bool required, bool initOnly)
    {
        var recordGenerator = new AvroRecordGenerator();

        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "record",
  "name" : "RoundTripWidget_{{required}}_{{initOnly}}",
  "namespace" : "Test.Avro.RoundTrip",
  "fields" : [
    { "name" : "id", "type" : "int" },
    { "name" : "name", "type" : "string" },
    { "name" : "nickname", "type" : [ "null", "string" ] }
  ]
}
""");

        var source = recordGenerator.Generate(schema, TestNamespace, new CodeGenOptions(RequiredProperties: required, InitOnlyProperties: initOnly));
        var generatedType = GeneratedSourceCompiler.CompileAndGetType(source, $"{TestNamespace}.RoundTripWidget_{required}_{initOnly}");

        // Mirrors Avro's own deserialization pattern (Avro.Specific.ObjectCreator uses
        // Activator.CreateInstance, not `new T()`, so `required` members impose no runtime cost here).
        var instance = (ISpecificRecord)Activator.CreateInstance(generatedType)!;
        instance.Put(0, 42);
        instance.Put(1, "hello");
        instance.Put(2, "world");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(instance.Get(0), Is.EqualTo(42));
            Assert.That(instance.Get(1), Is.EqualTo("hello"));
            Assert.That(instance.Get(2), Is.EqualTo("world"));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public static void Generate_GivenFieldNamesMatchingGetAndPutParameters_RoundTripsThroughPutAndGet(bool initOnly)
    {
        var recordGenerator = new AvroRecordGenerator();

        // "fieldPos" and "fieldValue" are the parameter names of the generated Get and Put methods,
        // so properties keeping those names would be shadowed inside the method bodies: Get would
        // return the index it was handed and Put would assign its parameter to itself, leaving both
        // fields at their defaults.
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "record",
  "name" : "ShadowingWidget_{{initOnly}}",
  "namespace" : "Test.Avro.RoundTrip",
  "fields" : [
    { "name" : "fieldValue", "type" : "string" },
    { "name" : "fieldPos", "type" : "int" }
  ]
}
""");

        var source = recordGenerator.Generate(schema, TestNamespace, new CodeGenOptions(InitOnlyProperties: initOnly));
        var generatedType = GeneratedSourceCompiler.CompileAndGetType(source, $"{TestNamespace}.ShadowingWidget_{initOnly}");

        var instance = (ISpecificRecord)Activator.CreateInstance(generatedType)!;
        instance.Put(0, "hello");
        instance.Put(1, 42);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(instance.Get(0), Is.EqualTo("hello"));
            Assert.That(instance.Get(1), Is.EqualTo(42));
        }
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public static void Generate_GivenErrorAndOptionCombination_ProducesTypeCompatibleWithConstructThenPut(bool required, bool initOnly)
    {
        var recordGenerator = new AvroRecordGenerator();

        // An error is generated as a class deriving from SpecificException rather than as a record,
        // so the accessors the options ask for are declared on a different shape of type.
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "error",
  "name" : "RoundTripFailure_{{required}}_{{initOnly}}",
  "namespace" : "Test.Avro.RoundTrip",
  "fields" : [
    { "name" : "code", "type" : "int" },
    { "name" : "reason", "type" : "string" },
    { "name" : "detail", "type" : [ "null", "string" ] }
  ]
}
""");

        var source = recordGenerator.Generate(schema, TestNamespace, new CodeGenOptions(RequiredProperties: required, InitOnlyProperties: initOnly));
        var generatedType = GeneratedSourceCompiler.CompileAndGetType(source, $"{TestNamespace}.RoundTripFailure_{required}_{initOnly}");

        var instance = (SpecificException)Activator.CreateInstance(generatedType)!;
        instance.Put(0, 42);
        instance.Put(1, "broken");
        instance.Put(2, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(instance.Get(0), Is.EqualTo(42));
            Assert.That(instance.Get(1), Is.EqualTo("broken"));
            Assert.That(instance.Get(2), Is.Null);
        }
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public static void Generate_GivenNullableDecimalAndOptionCombination_ProducesTypeCompatibleWithConstructThenPut(bool required, bool initOnly)
    {
        var recordGenerator = new AvroRecordGenerator();

        // A decimal is the one field type Put converts rather than casts, so the conversion has to
        // reach the backing field an init-only property writes through, and a nullable one must not
        // be treated as a member that has to be assigned at construction.
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "record",
  "name" : "RoundTripDecimalWidget_{{required}}_{{initOnly}}",
  "namespace" : "Test.Avro.RoundTrip",
  "fields" : [
    { "name" : "amount", "type" : { "type" : "bytes", "logicalType" : "decimal", "precision" : 10, "scale" : 2 } },
    { "name" : "discount", "type" : [ "null", { "type" : "bytes", "logicalType" : "decimal", "precision" : 10, "scale" : 2 } ] }
  ]
}
""");

        var source = recordGenerator.Generate(schema, TestNamespace, new CodeGenOptions(RequiredProperties: required, InitOnlyProperties: initOnly));
        var generatedType = GeneratedSourceCompiler.CompileAndGetType(source, $"{TestNamespace}.RoundTripDecimalWidget_{required}_{initOnly}");

        var instance = (ISpecificRecord)Activator.CreateInstance(generatedType)!;
        instance.Put(0, new AvroDecimal(1.25m));
        instance.Put(1, new AvroDecimal(0.75m));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(generatedType.GetProperty("discount")!.PropertyType, Is.EqualTo(typeof(decimal?)));
            Assert.That(instance.Get(0), Is.EqualTo(new AvroDecimal(1.25m)));
            Assert.That(instance.Get(1), Is.EqualTo(new AvroDecimal(0.75m)));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public static void Generate_GivenNullableDecimalLeftUnset_ReadsItBackAsNull(bool initOnly)
    {
        var recordGenerator = new AvroRecordGenerator();

        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "record",
  "name" : "RoundTripAbsentDecimalWidget_{{initOnly}}",
  "namespace" : "Test.Avro.RoundTrip",
  "fields" : [
    { "name" : "discount", "type" : [ "null", { "type" : "bytes", "logicalType" : "decimal", "precision" : 10, "scale" : 2 } ] }
  ]
}
""");

        var source = recordGenerator.Generate(schema, TestNamespace, new CodeGenOptions(InitOnlyProperties: initOnly));
        var generatedType = GeneratedSourceCompiler.CompileAndGetType(source, $"{TestNamespace}.RoundTripAbsentDecimalWidget_{initOnly}");

        var instance = (ISpecificRecord)Activator.CreateInstance(generatedType)!;
        instance.Put(0, null);

        Assert.That(instance.Get(0), Is.Null);
    }

    [Test]
    public static void Generate_GivenInitOnlyOptionWithFieldNamesCollidingWithBackingFieldConvention_StillCompiles()
    {
        var recordGenerator = new AvroRecordGenerator();

        // "schema" would collide with the hardcoded "_schema" field, and "x"/"_x" would otherwise
        // converge on the same backing field name without cross-field collision tracking.
        var schema = (RecordSchema)Schema.Parse("""
{
  "type" : "record",
  "name" : "CollidingWidget",
  "namespace" : "Test.Avro.RoundTrip",
  "fields" : [
    { "name" : "schema", "type" : "string" },
    { "name" : "x", "type" : "int" },
    { "name" : "_x", "type" : "int" }
  ]
}
""");

        var source = recordGenerator.Generate(schema, TestNamespace, new CodeGenOptions(InitOnlyProperties: true));
        var generatedType = GeneratedSourceCompiler.CompileAndGetType(source, $"{TestNamespace}.CollidingWidget");

        var instance = (ISpecificRecord)Activator.CreateInstance(generatedType)!;
        instance.Put(0, "hello");
        instance.Put(1, 1);
        instance.Put(2, 2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(instance.Get(0), Is.EqualTo("hello"));
            Assert.That(instance.Get(1), Is.EqualTo(1));
            Assert.That(instance.Get(2), Is.EqualTo(2));
        }
    }
}
