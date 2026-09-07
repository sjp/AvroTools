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
            Assert.That(generatedType.GetProperty("amounts")!.PropertyType, Is.EqualTo(typeof(List<AvroDecimal>)));
            Assert.That(generatedType.GetProperty("amountsByName")!.PropertyType, Is.EqualTo(typeof(IDictionary<string, AvroDecimal>)));
            Assert.That(((List<AvroDecimal>)deserialized.Get(0)).ConvertAll(AvroDecimal.ToDecimal), Is.EqualTo(new[] { 1.25m, 2.50m }));
            Assert.That(AvroDecimal.ToDecimal(((IDictionary<string, AvroDecimal>)deserialized.Get(1))["fee"]), Is.EqualTo(3.75m));
        }
    }

    private static ISpecificRecord RoundTrip(RecordSchema schema, ISpecificRecord record)
    {
        using var stream = new MemoryStream();
        new SpecificDatumWriter<ISpecificRecord>(schema).Write(record, new BinaryEncoder(stream));
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
}
