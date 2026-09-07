using System;
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
