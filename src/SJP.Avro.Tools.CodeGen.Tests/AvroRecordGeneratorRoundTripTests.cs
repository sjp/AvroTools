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
