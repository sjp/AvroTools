using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avro;
using Avro.Generic;
using Avro.IO;
using Avro.Specific;
using NUnit.Framework;

namespace SJP.Avro.Tools.CodeGen.Tests;

/// <summary>
/// Compiles the output of every generator and exercises it against <c>Apache.Avro</c>.
/// Text comparisons alone cannot tell whether generated code is accepted by the C# compiler,
/// so each generator is covered here by a compilation, and records additionally by a
/// serialization round trip through <see cref="SpecificDatumWriter{T}"/>.
/// </summary>
[TestFixture]
internal static class GeneratedCodeCompilationTests
{
    private const string TestNamespace = "Test.Avro.Compilation";

    [Test]
    public static void Generate_GivenFixedSchema_ProducesCompilableSpecificFixed()
    {
        var fixedGenerator = new AvroFixedGenerator();

        var schema = (FixedSchema)Schema.Parse($$"""
{
    "type": "fixed",
    "name": "CompiledHash",
    "namespace": "{{TestNamespace}}",
    "size": 16
}
""");

        var source = fixedGenerator.Generate(schema, TestNamespace);
        var generatedType = GeneratedSourceCompiler.CompileAndGetType(source, $"{TestNamespace}.CompiledHash");

        var instance = (SpecificFixed)Activator.CreateInstance(generatedType)!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(instance.Value, Has.Length.EqualTo(16));
            Assert.That(instance.Schema.Fullname, Is.EqualTo($"{TestNamespace}.CompiledHash"));
        }
    }

    [Test]
    public static void Generate_GivenErrorSchema_ProducesCompilableSpecificException()
    {
        var recordGenerator = new AvroRecordGenerator();

        var schema = (RecordSchema)Schema.Parse($$"""
{
    "type": "error",
    "name": "CompiledFailure",
    "namespace": "{{TestNamespace}}",
    "fields": [
        { "name": "message", "type": "string" }
    ]
}
""");

        var source = recordGenerator.Generate(schema, TestNamespace);
        var generatedType = GeneratedSourceCompiler.CompileAndGetType(source, $"{TestNamespace}.CompiledFailure");

        var instance = (SpecificException)Activator.CreateInstance(generatedType)!;
        instance.Put(0, "it broke");

        var caught = Assert.Catch<SpecificException>(() => throw instance);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(caught.Get(0), Is.EqualTo("it broke"));
            Assert.That(caught.Schema.Fullname, Is.EqualTo($"{TestNamespace}.CompiledFailure"));
        }
    }

    [Test]
    public static void Generate_GivenEnumSchema_ProducesCompilableEnum()
    {
        var enumGenerator = new AvroEnumGenerator();

        var schema = (EnumSchema)Schema.Parse($$"""
{
    "type": "enum",
    "name": "CompiledKind",
    "namespace": "{{TestNamespace}}",
    "symbols": [ "Small", "Large" ]
}
""");

        var source = enumGenerator.Generate(schema, TestNamespace);
        var generatedType = GeneratedSourceCompiler.CompileAndGetType(source, $"{TestNamespace}.CompiledKind");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(generatedType.IsEnum, Is.True);
            Assert.That(Enum.GetNames(generatedType), Is.EqualTo(new[] { "Small", "Large" }));
        }
    }

    [Test]
    public static void Generate_GivenNamespaceSegmentsThatAreKeywords_ProducesCompilableCrossNamespaceReferences()
    {
        // Avro namespaces admit segments that are C# keywords, and a record referring across them
        // has to escape those segments in its own declaration and in every reference it makes.
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "record",
  "name" : "CompiledKeywordNamespaceWidget",
  "namespace" : "{{TestNamespace}}.record",
  "fields" : [
    { "name" : "kind", "type" : { "type" : "enum", "name" : "CompiledKeywordKind", "namespace" : "{{TestNamespace}}.enum", "symbols" : [ "Small", "Large" ] } },
    { "name" : "hash", "type" : { "type" : "fixed", "name" : "CompiledKeywordHash", "namespace" : "{{TestNamespace}}.fixed", "size" : 4 } }
  ]
}
""");

        var enumSchema = (EnumSchema)schema.Fields[0].Schema;
        var fixedSchema = (FixedSchema)schema.Fields[1].Schema;

        var recordSource = new AvroRecordGenerator().Generate(schema, TestNamespace);
        var assembly = GeneratedSourceCompiler.Compile(
            new AvroEnumGenerator().Generate(enumSchema, TestNamespace),
            new AvroFixedGenerator().Generate(fixedSchema, TestNamespace),
            recordSource);

        var generatedType = assembly.GetType($"{TestNamespace}.record.CompiledKeywordNamespaceWidget")!;
        var enumType = assembly.GetType($"{TestNamespace}.enum.CompiledKeywordKind")!;
        var widget = (ISpecificRecord)Activator.CreateInstance(generatedType)!;
        widget.Put(0, Enum.Parse(enumType, "Large"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(recordSource, Does.Contain($"namespace {TestNamespace}.record"));
            Assert.That(recordSource, Does.Contain($"global::{TestNamespace}.@enum.CompiledKeywordKind"));
            Assert.That(recordSource, Does.Contain($"global::{TestNamespace}.@fixed.CompiledKeywordHash"));
            Assert.That(assembly.GetType($"{TestNamespace}.fixed.CompiledKeywordHash"), Is.Not.Null);
            Assert.That(widget.Get(0), Is.EqualTo(Enum.Parse(enumType, "Large")));
        }
    }

    [Test]
    public static void Generate_GivenNamesThatCollideWithReferencedTypes_ProducesCompilableCode()
    {
        // Avro names are unconstrained: a schema may declare two types of the same name in
        // different namespaces, and fields named after the very types the generated bodies use.
        // Naming every referenced and library type in full is what keeps those names unambiguous.
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "record",
  "name" : "CompiledCollidingNamesWidget",
  "namespace" : "{{TestNamespace}}",
  "fields" : [
    { "name" : "first", "type" : { "type" : "record", "name" : "Collision", "namespace" : "{{TestNamespace}}.a", "fields" : [] } },
    { "name" : "second", "type" : { "type" : "record", "name" : "Collision", "namespace" : "{{TestNamespace}}.b", "fields" : [] } },
    { "name" : "AvroDecimal", "type" : { "type" : "bytes", "logicalType" : "decimal", "precision" : 10, "scale" : 1 } },
    { "name" : "Math", "type" : "int" },
    { "name" : "MidpointRounding", "type" : "string" }
  ]
}
""");

        var recordGenerator = new AvroRecordGenerator();
        var assembly = GeneratedSourceCompiler.Compile(
            recordGenerator.Generate((RecordSchema)schema.Fields[0].Schema, TestNamespace),
            recordGenerator.Generate((RecordSchema)schema.Fields[1].Schema, TestNamespace),
            recordGenerator.Generate(schema, TestNamespace));

        var generatedType = assembly.GetType($"{TestNamespace}.CompiledCollidingNamesWidget")!;
        var widget = (ISpecificRecord)Activator.CreateInstance(generatedType)!;
        widget.Put(2, new AvroDecimal(1.5m));
        widget.Put(3, 7);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(generatedType.GetProperty("first")!.PropertyType.FullName, Is.EqualTo($"{TestNamespace}.a.Collision"));
            Assert.That(generatedType.GetProperty("second")!.PropertyType.FullName, Is.EqualTo($"{TestNamespace}.b.Collision"));
            Assert.That(widget.Get(2) is AvroDecimal d ? AvroDecimal.ToDecimal(d) : (decimal?)null, Is.EqualTo(1.5m));
            Assert.That(widget.Get(3), Is.EqualTo(7));
        }
    }

    [TestCase("P", 0)]
    [TestCase("C", 1)]
    [TestCase("CF", 2)]
    [TestCase("LF", 3)]
    public static void Generate_GivenEnumWithNonFirstDefault_ReadsEverySymbolBackAsItself(string symbol, int ordinal)
    {
        // An enum travels as the position of its symbol, and the specific reader turns that
        // position straight into the C# enum value, so schema order has to survive generation
        // even when the schema names a default other than its first symbol.
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "record",
  "name" : "CompiledDefaultedEnumWidget_{{symbol}}",
  "namespace" : "{{TestNamespace}}",
  "fields" : [
    { "name" : "pos", "type" : { "type" : "enum", "name" : "CompiledDefaultedPosition_{{symbol}}", "symbols" : [ "P", "C", "CF", "LF" ], "default" : "CF" } }
  ]
}
""");

        var enumSchema = (EnumSchema)schema.Fields[0].Schema;
        var assembly = GeneratedSourceCompiler.Compile(
            new AvroEnumGenerator().Generate(enumSchema, TestNamespace),
            new AvroRecordGenerator().Generate(schema, TestNamespace));

        // Each case declares its own type names: the specific reader resolves a generated type by
        // name across every loaded assembly, so reusing one name across cases would bind to
        // whichever assembly was compiled first.
        var enumType = assembly.GetType($"{TestNamespace}.CompiledDefaultedPosition_{symbol}")!;

        var written = new GenericRecord(schema);
        written.Add("pos", new GenericEnum(enumSchema, symbol));

        var deserialized = WriteGenericReadSpecific(schema, written);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Enum.GetNames(enumType), Is.EqualTo(new[] { "P", "C", "CF", "LF" }));
            Assert.That((int)Enum.Parse(enumType, symbol), Is.EqualTo(ordinal));
            Assert.That(deserialized.Get(0), Is.EqualTo(Enum.Parse(enumType, symbol)));
        }
    }

    [Test]
    public static void Generate_GivenProtocol_ProducesCompilableSpecificProtocol()
    {
        var protocolGenerator = new AvroProtocolGenerator();

        var protocol = Protocol.Parse($$"""
{
  "protocol" : "CompiledService",
  "namespace" : "{{TestNamespace}}",
  "types" : [ {
    "type" : "record",
    "name" : "CompiledRequest",
    "fields" : [ { "name" : "id", "type" : "int" } ]
  } ],
  "messages" : {
    "echo" : {
      "request" : [ { "name" : "payload", "type" : "CompiledRequest" } ],
      "response" : "string"
    },
    "notify" : {
      "request" : [ { "name" : "text", "type" : "string" } ],
      "response" : "null"
    }
  }
}
""");

        var recordGenerator = new AvroRecordGenerator();
        var requestSource = recordGenerator.Generate((RecordSchema)protocol.Types.Single(), TestNamespace);
        var protocolSource = protocolGenerator.Generate(protocol, TestNamespace);

        var assembly = GeneratedSourceCompiler.Compile(requestSource, protocolSource);
        var generatedType = assembly.GetType($"{TestNamespace}.CompiledService")!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(typeof(ISpecificProtocol).IsAssignableFrom(generatedType), Is.True);
            Assert.That(generatedType.IsAbstract, Is.True);
            Assert.That(generatedType.GetMethod("echo"), Is.Not.Null);
        }
    }

    [Test]
    public static void Generate_GivenUuidLogicalType_RoundTripsThroughSpecificDatumReaderAsGuid()
    {
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "record",
  "name" : "CompiledUuidWidget",
  "namespace" : "{{TestNamespace}}",
  "fields" : [
    { "name" : "id", "type" : { "type" : "string", "logicalType" : "uuid" } },
    { "name" : "parentId", "type" : [ "null", { "type" : "string", "logicalType" : "uuid" } ] }
  ]
}
""");

        var generatedType = GeneratedSourceCompiler.CompileAndGetType(
            new AvroRecordGenerator().Generate(schema, TestNamespace),
            $"{TestNamespace}.CompiledUuidWidget");

        var id = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        var widget = (ISpecificRecord)Activator.CreateInstance(generatedType)!;
        widget.Put(0, id);
        widget.Put(1, parentId);

        using var stream = new MemoryStream();
        new SpecificDatumWriter<ISpecificRecord>(schema).Write(widget, new BinaryEncoder(stream));
        stream.Seek(0, SeekOrigin.Begin);

        var deserialized = new SpecificDatumReader<ISpecificRecord>(schema, schema).Read(null!, new BinaryDecoder(stream));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(generatedType.GetProperty("id")!.PropertyType, Is.EqualTo(typeof(Guid)));
            Assert.That(generatedType.GetProperty("parentId")!.PropertyType, Is.EqualTo(typeof(Guid?)));
            Assert.That(deserialized.Get(0), Is.EqualTo(id));
            Assert.That(deserialized.Get(1), Is.EqualTo(parentId));
        }
    }

    [Test]
    public static void Generate_GivenDateAndTimeLogicalTypes_RoundTripThroughSpecificDatumReader()
    {
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "record",
  "name" : "CompiledTemporalWidget",
  "namespace" : "{{TestNamespace}}",
  "fields" : [
    { "name" : "day", "type" : { "type" : "int", "logicalType" : "date" } },
    { "name" : "timeMillis", "type" : { "type" : "int", "logicalType" : "time-millis" } },
    { "name" : "timeMicros", "type" : { "type" : "long", "logicalType" : "time-micros" } },
    { "name" : "stampMillis", "type" : { "type" : "long", "logicalType" : "timestamp-millis" } },
    { "name" : "stampMicros", "type" : { "type" : "long", "logicalType" : "timestamp-micros" } },
    { "name" : "localStampMillis", "type" : { "type" : "long", "logicalType" : "local-timestamp-millis" } },
    { "name" : "localStampMicros", "type" : { "type" : "long", "logicalType" : "local-timestamp-micros" } }
  ]
}
""");

        var generatedType = GeneratedSourceCompiler.CompileAndGetType(
            new AvroRecordGenerator().Generate(schema, TestNamespace),
            $"{TestNamespace}.CompiledTemporalWidget");

        var day = new DateTime(2024, 5, 17, 0, 0, 0, DateTimeKind.Utc);
        var timeOfDay = new TimeSpan(0, 13, 45, 30, 250);
        var stamp = new DateTime(2024, 5, 17, 13, 45, 30, DateTimeKind.Utc);
        var localStamp = DateTime.SpecifyKind(new DateTime(2024, 5, 17, 13, 45, 30), DateTimeKind.Local);

        var widget = (ISpecificRecord)Activator.CreateInstance(generatedType)!;
        widget.Put(0, day);
        widget.Put(1, timeOfDay);
        widget.Put(2, timeOfDay);
        widget.Put(3, stamp);
        widget.Put(4, stamp);
        widget.Put(5, localStamp);
        widget.Put(6, localStamp);

        var deserialized = RoundTrip(schema, widget);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(generatedType.GetProperty("day")!.PropertyType, Is.EqualTo(typeof(DateTime)));
            Assert.That(generatedType.GetProperty("timeMillis")!.PropertyType, Is.EqualTo(typeof(TimeSpan)));
            Assert.That(generatedType.GetProperty("timeMicros")!.PropertyType, Is.EqualTo(typeof(TimeSpan)));
            Assert.That(generatedType.GetProperty("stampMillis")!.PropertyType, Is.EqualTo(typeof(DateTime)));
            Assert.That(generatedType.GetProperty("stampMicros")!.PropertyType, Is.EqualTo(typeof(DateTime)));
            Assert.That(generatedType.GetProperty("localStampMillis")!.PropertyType, Is.EqualTo(typeof(DateTime)));
            Assert.That(generatedType.GetProperty("localStampMicros")!.PropertyType, Is.EqualTo(typeof(DateTime)));
            Assert.That(deserialized.Get(0), Is.EqualTo(day));
            Assert.That(deserialized.Get(1), Is.EqualTo(timeOfDay));
            Assert.That(deserialized.Get(2), Is.EqualTo(timeOfDay));
            Assert.That(deserialized.Get(3), Is.EqualTo(stamp));
            Assert.That(deserialized.Get(4), Is.EqualTo(stamp));
            Assert.That(deserialized.Get(5), Is.EqualTo(localStamp));
            Assert.That(deserialized.Get(6), Is.EqualTo(localStamp));
        }
    }

    [Test]
    public static void Generate_GivenDurationLogicalType_RoundTripsThroughSpecificDatumReaderAsItsFixedType()
    {
        // Avro implements no conversion for 'duration', so its values travel as the 12-byte fixed
        // backing it and the generated property has to be typed as that fixed rather than as a
        // TimeSpan the runtime would never hand over.
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "record",
  "name" : "CompiledDurationWidget",
  "namespace" : "{{TestNamespace}}",
  "fields" : [
    { "name" : "elapsed", "type" : { "type" : "fixed", "name" : "CompiledDuration", "size" : 12, "logicalType" : "duration" } }
  ]
}
""");

        var fixedSchema = (FixedSchema)((LogicalSchema)schema.Fields[0].Schema).BaseSchema;
        var assembly = GeneratedSourceCompiler.Compile(
            new AvroFixedGenerator().Generate(fixedSchema, TestNamespace),
            new AvroRecordGenerator().Generate(schema, TestNamespace));

        var generatedType = assembly.GetType($"{TestNamespace}.CompiledDurationWidget")!;
        var durationType = assembly.GetType($"{TestNamespace}.CompiledDuration")!;

        var elapsed = (GenericFixed)Activator.CreateInstance(durationType)!;
        elapsed.Value = [1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0];

        var widget = (ISpecificRecord)Activator.CreateInstance(generatedType)!;
        widget.Put(0, elapsed);

        var deserialized = RoundTrip(schema, widget);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(generatedType.GetProperty("elapsed")!.PropertyType, Is.EqualTo(durationType));
            Assert.That(((GenericFixed)deserialized.Get(0)).Value, Is.EqualTo(elapsed.Value));
        }
    }

    [Test]
    public static void Generate_GivenDecimalWithoutScale_RoundTripsThroughSpecificDatumReaderAtScaleZero()
    {
        // 'scale' is optional in Avro and defaults to zero.
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "record",
  "name" : "CompiledUnscaledWidget",
  "namespace" : "{{TestNamespace}}",
  "fields" : [
    { "name" : "count", "type" : { "type" : "bytes", "logicalType" : "decimal", "precision" : 10 } }
  ]
}
""");

        var generatedType = GeneratedSourceCompiler.CompileAndGetType(
            new AvroRecordGenerator().Generate(schema, TestNamespace),
            $"{TestNamespace}.CompiledUnscaledWidget");

        var widget = (ISpecificRecord)Activator.CreateInstance(generatedType)!;
        widget.Put(0, new AvroDecimal(7m));

        var deserialized = RoundTrip(schema, widget);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(generatedType.GetProperty("count")!.PropertyType, Is.EqualTo(typeof(decimal)));
            Assert.That(AvroDecimal.ToDecimal((AvroDecimal)deserialized.Get(0)), Is.EqualTo(7m));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public static void Generate_GivenNullableDecimal_RoundTripsThroughSpecificDatumReader(bool hasValue)
    {
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "record",
  "name" : "CompiledNullableDecimalWidget",
  "namespace" : "{{TestNamespace}}",
  "fields" : [
    { "name" : "amount", "type" : [ "null", { "type" : "bytes", "logicalType" : "decimal", "precision" : 10, "scale" : 2 } ] }
  ]
}
""");

        var generatedType = GeneratedSourceCompiler.CompileAndGetType(
            new AvroRecordGenerator().Generate(schema, TestNamespace),
            $"{TestNamespace}.CompiledNullableDecimalWidget");

        var widget = (ISpecificRecord)Activator.CreateInstance(generatedType)!;
        widget.Put(0, hasValue ? new AvroDecimal(1.25m) : null!);

        var deserialized = RoundTrip(schema, widget);
        var expected = hasValue ? (decimal?)1.25m : null;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(generatedType.GetProperty("amount")!.PropertyType, Is.EqualTo(typeof(decimal?)));
            Assert.That(generatedType.GetProperty("amount")!.GetValue(widget), Is.EqualTo(expected));
            Assert.That(deserialized.Get(0) is AvroDecimal d ? AvroDecimal.ToDecimal(d) : (decimal?)null, Is.EqualTo(expected));
        }
    }

    [TestCase(""" { "type" : "fixed", "name" : "CompiledMoney", "size" : 8, "logicalType" : "decimal", "precision" : 10, "scale" : 2 } """)]
    [TestCase(""" [ "null", { "type" : "fixed", "name" : "CompiledMoney", "size" : 8, "logicalType" : "decimal", "precision" : 10, "scale" : 2 } ] """)]
    [TestCase(""" { "type" : "array", "items" : { "type" : "fixed", "name" : "CompiledMoney", "size" : 8, "logicalType" : "decimal", "precision" : 10, "scale" : 2 } } """)]
    public static void Generate_GivenFixedBackedDecimal_ThrowsRatherThanEmittingUnusableCode(string fieldType)
    {
        // Avro converts a decimal stored in a fixed to and from a generic fixed, which its specific
        // writer rejects ("Fixed object is not derived from SpecificFixed") and which its specific
        // reader cannot cast to the generated class. No generated member type can bridge that, so
        // the schema is refused instead of producing code that throws on first use.
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "record",
  "name" : "CompiledFixedDecimalWidget",
  "namespace" : "{{TestNamespace}}",
  "fields" : [
    { "name" : "amount", "type" : {{fieldType}} }
  ]
}
""");

        var exception = Assert.Throws<NotSupportedException>(
            () => new AvroRecordGenerator().Generate(schema, TestNamespace));

        Assert.That(exception!.Message, Does.Contain($"{TestNamespace}.CompiledMoney"));
    }

    [Test]
    public static void Generate_GivenLogicalTypeBackedByFixed_EmbedsTheSchemaInItsPortableForm()
    {
        // Avro writes a logical type over a named type as a wrapper around it, a spelling most
        // Avro implementations reject. The embedded schema carries the logical attributes on the
        // fixed itself, which is what the specification defines and what Avro parses back.
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "record",
  "name" : "CompiledPortableDurationWidget",
  "namespace" : "{{TestNamespace}}",
  "fields" : [
    { "name" : "elapsed", "type" : { "type" : "fixed", "name" : "CompiledPortableDuration", "size" : 12, "logicalType" : "duration" } }
  ]
}
""");

        var source = new AvroRecordGenerator().Generate(schema, TestNamespace);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(source, Does.Contain(
                """{\"type\":\"fixed\",\"name\":\"CompiledPortableDuration\",\"namespace\":\"Test.Avro.Compilation\",\"size\":12,\"logicalType\":\"duration\"}"""));
            Assert.That(source, Does.Not.Contain("""{\"type\":{\"type\":\"fixed"""));
        }
    }

    [Test]
    public static void Generate_GivenDecimalsInsideCollections_RoundTripsThroughSpecificDatumReaderAsAvroDecimal()
    {
        // Values inside an array or a map stay in Avro's own representation rather than being
        // converted element by element, so the properties are typed in terms of AvroDecimal.
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "record",
  "name" : "CompiledDecimalCollectionWidget",
  "namespace" : "{{TestNamespace}}",
  "fields" : [
    { "name" : "amounts", "type" : { "type" : "array", "items" : { "type" : "bytes", "logicalType" : "decimal", "precision" : 10, "scale" : 2 } } },
    { "name" : "amountsByName", "type" : { "type" : "map", "values" : { "type" : "bytes", "logicalType" : "decimal", "precision" : 10, "scale" : 2 } } }
  ]
}
""");

        var generatedType = GeneratedSourceCompiler.CompileAndGetType(
            new AvroRecordGenerator().Generate(schema, TestNamespace),
            $"{TestNamespace}.CompiledDecimalCollectionWidget");

        var widget = (ISpecificRecord)Activator.CreateInstance(generatedType)!;
        widget.Put(0, new List<AvroDecimal> { new(1.25m), new(2.50m) });
        widget.Put(1, new Dictionary<string, AvroDecimal> { ["fee"] = new(3.75m) });

        var deserialized = RoundTrip(schema, widget);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(generatedType.GetProperty("amounts")!.PropertyType, Is.EqualTo(typeof(IList<AvroDecimal>)));
            Assert.That(generatedType.GetProperty("amountsByName")!.PropertyType, Is.EqualTo(typeof(IDictionary<string, AvroDecimal>)));
            Assert.That(((IList<AvroDecimal>)deserialized.Get(0)).Select(AvroDecimal.ToDecimal), Is.EqualTo(new[] { 1.25m, 2.50m }));
            Assert.That(AvroDecimal.ToDecimal(((IDictionary<string, AvroDecimal>)deserialized.Get(1))["fee"]), Is.EqualTo(3.75m));
        }
    }

    [TestCase("CompiledUnionAlpha")]
    [TestCase("CompiledUnionBeta")]
    public static void Generate_GivenUnionOfTwoRecords_RoundTripsEitherBranchThroughSpecificDatumReader(string branchName)
    {
        // Both branches are records, so a property typed as either one of them would fail the cast
        // in Put as soon as the other branch arrived.
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "record",
  "name" : "CompiledUnionWidget",
  "namespace" : "{{TestNamespace}}",
  "fields" : [
    { "name" : "v", "type" : [
      "null",
      { "type" : "record", "name" : "CompiledUnionAlpha", "fields" : [ { "name" : "a", "type" : "int" } ] },
      { "type" : "record", "name" : "CompiledUnionBeta", "fields" : [ { "name" : "b", "type" : "string" } ] } ] }
  ]
}
""");

        var recordGenerator = new AvroRecordGenerator();
        var branchSchemas = ((UnionSchema)schema.Fields[0].Schema).Schemas
            .OfType<RecordSchema>()
            .Select(s => recordGenerator.Generate(s, TestNamespace));

        var assembly = GeneratedSourceCompiler.Compile(
            branchSchemas.Append(recordGenerator.Generate(schema, TestNamespace)).ToArray());

        var generatedType = assembly.GetType($"{TestNamespace}.CompiledUnionWidget")!;
        var branch = (ISpecificRecord)Activator.CreateInstance(assembly.GetType($"{TestNamespace}.{branchName}")!)!;
        branch.Put(0, branchName == "CompiledUnionAlpha" ? 42 : "hello");

        var widget = (ISpecificRecord)Activator.CreateInstance(generatedType)!;
        widget.Put(0, branch);

        var deserialized = RoundTrip(schema, widget);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(generatedType.GetProperty("v")!.PropertyType, Is.EqualTo(typeof(object)));
            Assert.That(((ISpecificRecord)deserialized.Get(0)).Schema.Fullname, Is.EqualTo($"{TestNamespace}.{branchName}"));
            Assert.That(((ISpecificRecord)deserialized.Get(0)).Get(0), Is.EqualTo(branch.Get(0)));
        }
    }

    [Test]
    public static void Generate_GivenCollectionsNestedInsideCollections_RoundTripThroughSpecificDatumReader()
    {
        // Avro builds the container for a nested array out of the element's interface type: an
        // array of arrays arrives as List<IList<int>> and a map of arrays as
        // Dictionary<string, IList<int>>. Generic collections are invariant, so members typed
        // List<List<int>> would fail the cast in both Put and Get.
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "record",
  "name" : "CompiledNestedCollectionWidget",
  "namespace" : "{{TestNamespace}}",
  "fields" : [
    { "name" : "grid", "type" : { "type" : "array", "items" : { "type" : "array", "items" : "int" } } },
    { "name" : "rowsByName", "type" : { "type" : "map", "values" : { "type" : "array", "items" : "int" } } },
    { "name" : "optionalRows", "type" : { "type" : "array", "items" : [ "null", { "type" : "array", "items" : "int" } ] } }
  ]
}
""");

        var generatedType = GeneratedSourceCompiler.CompileAndGetType(
            new AvroRecordGenerator().Generate(schema, TestNamespace),
            $"{TestNamespace}.CompiledNestedCollectionWidget");

        var widget = (ISpecificRecord)Activator.CreateInstance(generatedType)!;
        widget.Put(0, new List<IList<int>> { new List<int> { 1, 2 }, new List<int> { 3 } });
        widget.Put(1, new Dictionary<string, IList<int>> { ["first"] = new List<int> { 4, 5 } });
        widget.Put(2, new List<IList<int>> { new List<int> { 6 }, null! });

        var deserialized = RoundTrip(schema, widget);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(generatedType.GetProperty("grid")!.PropertyType, Is.EqualTo(typeof(IList<IList<int>>)));
            Assert.That(generatedType.GetProperty("rowsByName")!.PropertyType, Is.EqualTo(typeof(IDictionary<string, IList<int>>)));
            Assert.That(generatedType.GetProperty("optionalRows")!.PropertyType, Is.EqualTo(typeof(IList<IList<int>>)));

            Assert.That(((IList<IList<int>>)deserialized.Get(0)).Select(row => row.ToArray()), Is.EqualTo(new[] { new[] { 1, 2 }, new[] { 3 } }));
            Assert.That(((IDictionary<string, IList<int>>)deserialized.Get(1))["first"], Is.EqualTo(new[] { 4, 5 }));
            Assert.That(((IList<IList<int>>)deserialized.Get(2)).Select(row => row?.ToArray()), Is.EqualTo(new[] { new[] { 6 }, null }));
        }
    }

    [Test]
    public static void Generate_GivenCollectionsNestedInsideCollections_ReadTheGenericRepresentationBack()
    {
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "record",
  "name" : "CompiledGenericNestedCollectionWidget",
  "namespace" : "{{TestNamespace}}",
  "fields" : [
    { "name" : "grid", "type" : { "type" : "array", "items" : { "type" : "array", "items" : "int" } } },
    { "name" : "rowsByName", "type" : { "type" : "map", "values" : { "type" : "array", "items" : "int" } } }
  ]
}
""");

        GeneratedSourceCompiler.CompileAndGetType(
            new AvroRecordGenerator().Generate(schema, TestNamespace),
            $"{TestNamespace}.CompiledGenericNestedCollectionWidget");

        var written = new GenericRecord(schema);
        written.Add("grid", new object[] { new object[] { 1, 2 }, new object[] { 3 } });
        written.Add("rowsByName", new Dictionary<string, object> { ["first"] = new object[] { 4, 5 } });

        var deserialized = WriteGenericReadSpecific(schema, written);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(((IList<IList<int>>)deserialized.Get(0)).Select(row => row.ToArray()), Is.EqualTo(new[] { new[] { 1, 2 }, new[] { 3 } }));
            Assert.That(((IDictionary<string, IList<int>>)deserialized.Get(1))["first"], Is.EqualTo(new[] { 4, 5 }));
        }
    }

    private static ISpecificRecord RoundTrip(RecordSchema schema, ISpecificRecord record)
    {
        using var stream = new MemoryStream();
        new SpecificDatumWriter<ISpecificRecord>(schema).Write(record, new BinaryEncoder(stream));
        stream.Seek(0, SeekOrigin.Begin);

        return new SpecificDatumReader<ISpecificRecord>(schema, schema).Read(null!, new BinaryDecoder(stream));
    }

    /// <summary>
    /// Writes a record in Avro's generic representation and reads it back through the specific
    /// reader, which resolves each named type to a generated class.
    /// </summary>
    private static ISpecificRecord WriteGenericReadSpecific(RecordSchema schema, GenericRecord record)
    {
        using var stream = new MemoryStream();
        new GenericDatumWriter<GenericRecord>(schema).Write(record, new BinaryEncoder(stream));
        stream.Seek(0, SeekOrigin.Begin);

        return new SpecificDatumReader<ISpecificRecord>(schema, schema).Read(null!, new BinaryDecoder(stream));
    }

    [TestCase(false, false)]
    [TestCase(true, true)]
    public static void Generate_GivenFieldsNamedAfterKeywords_ProducesCompilableProperties(bool requiredProperties, bool initOnlyProperties)
    {
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "record",
  "name" : "CompiledKeywordWidget",
  "namespace" : "{{TestNamespace}}",
  "fields" : [
    { "name" : "class", "type" : "string" },
    { "name" : "event", "type" : "string" },
    { "name" : "record", "type" : "string" }
  ]
}
""");

        var options = new CodeGenOptions(requiredProperties, initOnlyProperties);
        var generatedType = GeneratedSourceCompiler.CompileAndGetType(
            new AvroRecordGenerator().Generate(schema, TestNamespace, options),
            $"{TestNamespace}.CompiledKeywordWidget");

        var widget = (ISpecificRecord)Activator.CreateInstance(generatedType)!;
        widget.Put(0, "a");
        widget.Put(1, "b");
        widget.Put(2, "c");

        var deserialized = RoundTrip(schema, widget);

        using (Assert.EnterMultipleScope())
        {
            // The verbatim prefix is lexical only, so the members still carry the Avro names.
            Assert.That(generatedType.GetProperty("class"), Is.Not.Null);
            Assert.That(generatedType.GetProperty("event"), Is.Not.Null);
            Assert.That(generatedType.GetProperty("record"), Is.Not.Null);
            Assert.That(deserialized.Get(0), Is.EqualTo("a"));
            Assert.That(deserialized.Get(1), Is.EqualTo("b"));
            Assert.That(deserialized.Get(2), Is.EqualTo("c"));
        }
    }

    [Test]
    public static void Generate_GivenRecordNamedAfterKeyword_ProducesCompilableRecord()
    {
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "record",
  "name" : "event",
  "namespace" : "{{TestNamespace}}",
  "fields" : [
    { "name" : "id", "type" : "int" }
  ]
}
""");

        var generatedType = GeneratedSourceCompiler.CompileAndGetType(
            new AvroRecordGenerator().Generate(schema, TestNamespace),
            $"{TestNamespace}.event");

        var instance = (ISpecificRecord)Activator.CreateInstance(generatedType)!;
        instance.Put(0, 42);

        Assert.That(RoundTrip(schema, instance).Get(0), Is.EqualTo(42));
    }

    [Test]
    public static void Generate_GivenEnumSymbolNamedAfterKeyword_ProducesCompilableEnum()
    {
        var schema = (EnumSchema)Schema.Parse($$"""
{
    "type": "enum",
    "name": "CompiledKeywordKind",
    "namespace": "{{TestNamespace}}",
    "symbols": [ "int", "string" ]
}
""");

        var source = new AvroEnumGenerator().Generate(schema, TestNamespace);
        var generatedType = GeneratedSourceCompiler.CompileAndGetType(source, $"{TestNamespace}.CompiledKeywordKind");

        Assert.That(Enum.GetNames(generatedType), Is.EqualTo(new[] { "int", "string" }));
    }

    [Test]
    public static void Generate_GivenFixedNamedAfterKeyword_ProducesCompilableSpecificFixed()
    {
        var schema = (FixedSchema)Schema.Parse($$"""
{
    "type": "fixed",
    "name": "checked",
    "namespace": "{{TestNamespace}}",
    "size": 8
}
""");

        var source = new AvroFixedGenerator().Generate(schema, TestNamespace);
        var generatedType = GeneratedSourceCompiler.CompileAndGetType(source, $"{TestNamespace}.checked");

        var instance = (SpecificFixed)Activator.CreateInstance(generatedType)!;

        Assert.That(instance.Value, Has.Length.EqualTo(8));
    }

    [Test]
    public static void Generate_GivenMessageNamedAfterKeyword_ProducesCompilableSpecificProtocol()
    {
        var protocol = Protocol.Parse($$"""
{
  "protocol" : "CompiledKeywordService",
  "namespace" : "{{TestNamespace}}",
  "types" : [],
  "messages" : {
    "lock" : {
      "request" : [ { "name" : "for", "type" : "string" } ],
      "response" : "null"
    }
  }
}
""");

        var source = new AvroProtocolGenerator().Generate(protocol, TestNamespace);
        var generatedType = GeneratedSourceCompiler.CompileAndGetType(source, $"{TestNamespace}.CompiledKeywordService");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(generatedType.GetMethod("lock"), Is.Not.Null);
            Assert.That(generatedType.GetMethod("lock")!.GetParameters()[0].Name, Is.EqualTo("for"));
        }
    }

    [Test]
    public static void Generate_GivenFieldNamedAfterItsRecord_ProducesCompilableSuffixedProperty()
    {
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "record",
  "name" : "Foo",
  "namespace" : "{{TestNamespace}}",
  "fields" : [
    { "name" : "Foo", "type" : "string" },
    { "name" : "Schema", "type" : "int" }
  ]
}
""");

        var generatedType = GeneratedSourceCompiler.CompileAndGetType(
            new AvroRecordGenerator().Generate(schema, TestNamespace),
            $"{TestNamespace}.Foo");

        var instance = (ISpecificRecord)Activator.CreateInstance(generatedType)!;
        instance.Put(0, "bar");
        instance.Put(1, 3);

        var deserialized = RoundTrip(schema, instance);

        using (Assert.EnterMultipleScope())
        {
            // A member cannot share the name of its declaring type, nor of a member already
            // generated, so both properties are suffixed while the Avro names are untouched.
            Assert.That(generatedType.GetProperty("Foo_"), Is.Not.Null);
            Assert.That(generatedType.GetProperty("Schema_"), Is.Not.Null);
            Assert.That(deserialized.Get(0), Is.EqualTo("bar"));
            Assert.That(deserialized.Get(1), Is.EqualTo(3));
        }
    }

    [Test]
    public static void Generate_GivenMessageNamedAfterItsProtocol_ProducesCompilableSuffixedMethod()
    {
        var protocol = Protocol.Parse($$"""
{
  "protocol" : "Ping",
  "namespace" : "{{TestNamespace}}",
  "types" : [],
  "messages" : {
    "Ping" : { "request" : [], "response" : "null" },
    "Request" : { "request" : [], "response" : "null" }
  }
}
""");

        var source = new AvroProtocolGenerator().Generate(protocol, TestNamespace);
        var generatedType = GeneratedSourceCompiler.CompileAndGetType(source, $"{TestNamespace}.Ping");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(generatedType.GetMethod("Ping_"), Is.Not.Null);
            Assert.That(generatedType.GetMethod("Request_"), Is.Not.Null);
        }
    }

    [Test]
    public static void Generate_GivenRecordReferringToFixedAndEnum_RoundTripsThroughSpecificDatumReader()
    {
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "record",
  "name" : "CompiledWidget",
  "namespace" : "{{TestNamespace}}",
  "fields" : [
    { "name" : "id", "type" : "int" },
    { "name" : "hash", "type" : { "type" : "fixed", "name" : "WidgetHash", "namespace" : "{{TestNamespace}}", "size" : 4 } },
    { "name" : "kind", "type" : { "type" : "enum", "name" : "WidgetKind", "namespace" : "{{TestNamespace}}", "symbols" : [ "Small", "Large" ] } }
  ]
}
""");

        var fixedSchema = (FixedSchema)schema.Fields[1].Schema;
        var enumSchema = (EnumSchema)schema.Fields[2].Schema;

        var assembly = GeneratedSourceCompiler.Compile(
            new AvroRecordGenerator().Generate(schema, TestNamespace),
            new AvroFixedGenerator().Generate(fixedSchema, TestNamespace),
            new AvroEnumGenerator().Generate(enumSchema, TestNamespace));

        var hash = (GenericFixed)Activator.CreateInstance(assembly.GetType($"{TestNamespace}.WidgetHash")!)!;
        hash.Value = [1, 2, 3, 4];

        var widget = (ISpecificRecord)Activator.CreateInstance(assembly.GetType($"{TestNamespace}.CompiledWidget")!)!;
        widget.Put(0, 42);
        widget.Put(1, hash);
        widget.Put(2, Enum.Parse(assembly.GetType($"{TestNamespace}.WidgetKind")!, "Large"));

        using var stream = new MemoryStream();
        new SpecificDatumWriter<ISpecificRecord>(schema).Write(widget, new BinaryEncoder(stream));
        stream.Seek(0, SeekOrigin.Begin);

        var deserialized = new SpecificDatumReader<ISpecificRecord>(schema, schema).Read(null!, new BinaryDecoder(stream));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deserialized.Get(0), Is.EqualTo(42));
            Assert.That(((GenericFixed)deserialized.Get(1)).Value, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            Assert.That(deserialized.Get(2).ToString(), Is.EqualTo("Large"));
        }
    }

    [Test]
    public static void Generate_GivenFieldsNamedAfterRecordMembers_ProducesCompilableSuffixedProperties()
    {
        // A generated record inherits members from object and is filled out with more by the
        // compiler. A field named after one of those is a duplicate definition or hides an
        // inherited member, so it is the property that gives way.
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "record",
  "name" : "CompiledRecordMemberWidget",
  "namespace" : "{{TestNamespace}}",
  "fields" : [
    { "name" : "Equals", "type" : "string" },
    { "name" : "GetHashCode", "type" : "int" },
    { "name" : "ToString", "type" : "string" },
    { "name" : "GetType", "type" : "string" },
    { "name" : "Clone", "type" : "string" },
    { "name" : "EqualityContract", "type" : "string" },
    { "name" : "PrintMembers", "type" : "string" }
  ]
}
""");

        var generatedType = GeneratedSourceCompiler.CompileAndGetType(
            new AvroRecordGenerator().Generate(schema, TestNamespace),
            $"{TestNamespace}.CompiledRecordMemberWidget");

        var instance = (ISpecificRecord)Activator.CreateInstance(generatedType)!;
        instance.Put(0, "eq");
        instance.Put(1, 7);
        instance.Put(2, "str");
        instance.Put(3, "type");
        instance.Put(4, "clone");
        instance.Put(5, "contract");
        instance.Put(6, "print");

        var deserialized = RoundTrip(schema, instance);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(generatedType.GetProperty("Equals_"), Is.Not.Null);
            Assert.That(generatedType.GetProperty("GetHashCode_"), Is.Not.Null);
            Assert.That(generatedType.GetProperty("ToString_"), Is.Not.Null);
            Assert.That(generatedType.GetProperty("GetType_"), Is.Not.Null);
            Assert.That(generatedType.GetProperty("Clone_"), Is.Not.Null);
            Assert.That(generatedType.GetProperty("EqualityContract_"), Is.Not.Null);
            Assert.That(generatedType.GetProperty("PrintMembers_"), Is.Not.Null);
            Assert.That(deserialized.Get(0), Is.EqualTo("eq"));
            Assert.That(deserialized.Get(1), Is.EqualTo(7));
            Assert.That(deserialized.Get(6), Is.EqualTo("print"));
        }
    }

    [Test]
    public static void Generate_GivenErrorFieldsNamedAfterExceptionMembers_ProducesCompilableSuffixedProperties()
    {
        // An error is generated as a class deriving from SpecificException, so it carries every
        // member Exception has. Several of them are ordinary Avro field names.
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "error",
  "name" : "CompiledExceptionMemberFailure",
  "namespace" : "{{TestNamespace}}",
  "fields" : [
    { "name" : "Message", "type" : "string" },
    { "name" : "Data", "type" : "string" },
    { "name" : "Source", "type" : "string" },
    { "name" : "HResult", "type" : "int" },
    { "name" : "StackTrace", "type" : "string" },
    { "name" : "InnerException", "type" : "string" },
    { "name" : "TargetSite", "type" : "string" },
    { "name" : "HelpLink", "type" : "string" }
  ]
}
""");

        var generatedType = GeneratedSourceCompiler.CompileAndGetType(
            new AvroRecordGenerator().Generate(schema, TestNamespace),
            $"{TestNamespace}.CompiledExceptionMemberFailure");

        var instance = (SpecificException)Activator.CreateInstance(generatedType)!;
        instance.Put(0, "broken");
        instance.Put(3, 5);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(generatedType.GetProperty("Message_"), Is.Not.Null);
            Assert.That(generatedType.GetProperty("Data_"), Is.Not.Null);
            Assert.That(generatedType.GetProperty("Source_"), Is.Not.Null);
            Assert.That(generatedType.GetProperty("HResult_"), Is.Not.Null);
            Assert.That(generatedType.GetProperty("StackTrace_"), Is.Not.Null);
            Assert.That(generatedType.GetProperty("InnerException_"), Is.Not.Null);
            Assert.That(generatedType.GetProperty("TargetSite_"), Is.Not.Null);
            Assert.That(generatedType.GetProperty("HelpLink_"), Is.Not.Null);
            Assert.That(instance.Get(0), Is.EqualTo("broken"));
            Assert.That(instance.Get(3), Is.EqualTo(5));

            // The Avro field has not displaced what Exception itself reports.
            Assert.That(instance.Message, Is.Not.EqualTo("broken"));
        }
    }

    [TestCase("Schema")]
    [TestCase("Get")]
    [TestCase("Put")]
    public static void Generate_GivenRecordNamedAfterAnInterfaceMember_KeepsItsAvroName(string typeName)
    {
        // Avro looks a generated type up by the name its schema gave it, so the type has to keep
        // that name. It implements the member it is named after explicitly instead, which a C#
        // type is allowed to do even where it may not declare a member of its own name.
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "record",
  "name" : "{{typeName}}",
  "namespace" : "{{TestNamespace}}.member",
  "fields" : [
    { "name" : "value", "type" : "int" }
  ]
}
""");

        var generatedType = GeneratedSourceCompiler.CompileAndGetType(
            new AvroRecordGenerator().Generate(schema, TestNamespace),
            $"{TestNamespace}.member.{typeName}");

        var instance = (ISpecificRecord)Activator.CreateInstance(generatedType)!;
        instance.Put(0, 11);

        // Reading with no instance to reuse makes Avro resolve the type by its Avro name.
        var deserialized = RoundTrip(schema, instance);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(generatedType.Name, Is.EqualTo(typeName));
            Assert.That(deserialized.Get(0), Is.EqualTo(11));
            Assert.That(deserialized.Schema.Fullname, Is.EqualTo($"{TestNamespace}.member.{typeName}"));
        }
    }

    [TestCase("Protocol")]
    [TestCase("Request")]
    public static void Generate_GivenProtocolNamedAfterAnInterfaceMember_KeepsItsAvroName(string typeName)
    {
        var protocol = Protocol.Parse($$"""
{
  "protocol" : "{{typeName}}",
  "namespace" : "{{TestNamespace}}.member",
  "types" : [],
  "messages" : {
    "ping" : { "request" : [], "response" : "null" }
  }
}
""");

        var generatedType = GeneratedSourceCompiler.CompileAndGetType(
            new AvroProtocolGenerator().Generate(protocol, TestNamespace),
            $"{TestNamespace}.member.{typeName}");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(generatedType.Name, Is.EqualTo(typeName));
            Assert.That(typeof(ISpecificProtocol).IsAssignableFrom(generatedType), Is.True);
            Assert.That(generatedType.GetMethod("ping"), Is.Not.Null);
        }
    }

    [TestCase("Equals")]
    [TestCase("GetHashCode")]
    [TestCase("Clone")]
    [TestCase("EqualityContract")]
    public static void Generate_GivenMessageNamedAfterARecordMember_ProducesCompilableSuffixedMethod(string messageName)
    {
        var protocol = Protocol.Parse($$"""
{
  "protocol" : "CompiledMemberService_{{messageName}}",
  "namespace" : "{{TestNamespace}}",
  "types" : [],
  "messages" : {
    "{{messageName}}" : { "request" : [], "response" : "null" }
  }
}
""");

        var generatedType = GeneratedSourceCompiler.CompileAndGetType(
            new AvroProtocolGenerator().Generate(protocol, TestNamespace),
            $"{TestNamespace}.CompiledMemberService_{messageName}");

        Assert.That(generatedType.GetMethod(messageName + "_"), Is.Not.Null);
    }

    [Test]
    public static void Generate_GivenRecordNamedAfterItsSchemaField_ProducesCompilableCode()
    {
        // An Avro name may begin with an underscore, so a record can be named after the private
        // field the generator holds the parsed schema in. The field is the generator's own, so it
        // is the field that moves aside.
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "record",
  "name" : "_schema",
  "namespace" : "{{TestNamespace}}.member",
  "fields" : [
    { "name" : "value", "type" : "int" }
  ]
}
""");

        var generatedType = GeneratedSourceCompiler.CompileAndGetType(
            new AvroRecordGenerator().Generate(schema, TestNamespace),
            $"{TestNamespace}.member._schema");

        var instance = (ISpecificRecord)Activator.CreateInstance(generatedType)!;
        instance.Put(0, 3);

        Assert.That(RoundTrip(schema, instance).Get(0), Is.EqualTo(3));
    }

    [Test]
    public static void Generate_GivenFieldNamedAfterTheFieldPositionEnum_ProducesCompilableCode()
    {
        // The field position enum is named after the record, so a field of that name pushes the
        // enum aside; the backing field the init accessor writes through must then avoid both.
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "record",
  "name" : "CompiledEnumNameWidget",
  "namespace" : "{{TestNamespace}}",
  "fields" : [
    { "name" : "CompiledEnumNameWidgetField", "type" : "int" },
    { "name" : "compiledEnumNameWidgetField", "type" : "string" }
  ]
}
""");

        var generatedType = GeneratedSourceCompiler.CompileAndGetType(
            new AvroRecordGenerator().Generate(schema, TestNamespace, new CodeGenOptions(InitOnlyProperties: true)),
            $"{TestNamespace}.CompiledEnumNameWidget");

        var instance = (ISpecificRecord)Activator.CreateInstance(generatedType)!;
        instance.Put(0, 9);
        instance.Put(1, "here");

        var deserialized = RoundTrip(schema, instance);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deserialized.Get(0), Is.EqualTo(9));
            Assert.That(deserialized.Get(1), Is.EqualTo("here"));
        }
    }

    [Test]
    public static void Generate_GivenFixedNamedAfterItsSizeProperty_ProducesCompilableCode()
    {
        var schema = (FixedSchema)Schema.Parse($$"""
{
    "type": "fixed",
    "name": "FixedSize",
    "namespace": "{{TestNamespace}}.member",
    "size": 8
}
""");

        var generatedType = GeneratedSourceCompiler.CompileAndGetType(
            new AvroFixedGenerator().Generate(schema, TestNamespace),
            $"{TestNamespace}.member.FixedSize");

        var instance = (SpecificFixed)Activator.CreateInstance(generatedType)!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(generatedType.Name, Is.EqualTo("FixedSize"));
            Assert.That(instance.Value, Has.Length.EqualTo(8));
        }
    }

    [TestCase("Schema")]
    [TestCase("Get")]
    [TestCase("Put")]
    public static void Generate_GivenErrorNamedAfterAnAbstractBaseMember_ReportsThatItCannotBeGenerated(string typeName)
    {
        // An error derives from SpecificException, whose Schema, Get and Put are abstract, so it
        // has to declare members of those names. Renaming the type instead would break the lookup
        // Avro does by name, so the schema is refused rather than generated wrong.
        var schema = (RecordSchema)Schema.Parse($$"""
{
  "type" : "error",
  "name" : "{{typeName}}",
  "namespace" : "{{TestNamespace}}.member",
  "fields" : []
}
""");

        var generator = new AvroRecordGenerator();

        Assert.That(() => generator.Generate(schema, TestNamespace), Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public static void Generate_GivenFixedNamedAfterItsSchemaProperty_ReportsThatItCannotBeGenerated()
    {
        var schema = (FixedSchema)Schema.Parse($$"""
{
    "type": "fixed",
    "name": "Schema",
    "namespace": "{{TestNamespace}}.member",
    "size": 4
}
""");

        var generator = new AvroFixedGenerator();

        Assert.That(() => generator.Generate(schema, TestNamespace), Throws.TypeOf<NotSupportedException>());
    }
}
