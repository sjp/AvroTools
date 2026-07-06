using Avro;
using Avro.Generic;
using Avro.Util;
using NUnit.Framework;

namespace SJP.Avro.Tools.Tests;

[TestFixture]
internal static class AvroJsonWriterTests
{
    [Test]
    public static void Encode_GivenPrimitiveInUnion_WrapsWithTypeName()
    {
        const string schemaJson = """
{
  "type": "record",
  "name": "Person",
  "fields": [
    { "name": "nickname", "type": ["null", "string"], "default": null }
  ]
}
""";

        var schema = (RecordSchema)Schema.Parse(schemaJson);
        var record = new GenericRecord(schema);
        record.Add("nickname", "Bobby");

        var json = AvroJsonWriter.Encode(schema, record);

        Assert.That(json, Is.EqualTo("""{"nickname":{"string":"Bobby"}}"""));
    }

    [Test]
    public static void Encode_GivenNullInUnion_WritesNull()
    {
        const string schemaJson = """
{
  "type": "record",
  "name": "Person",
  "fields": [
    { "name": "nickname", "type": ["null", "string"], "default": null }
  ]
}
""";

        var schema = (RecordSchema)Schema.Parse(schemaJson);
        var record = new GenericRecord(schema);
        record.Add("nickname", null);

        var json = AvroJsonWriter.Encode(schema, record);

        Assert.That(json, Is.EqualTo("""{"nickname":null}"""));
    }

    [Test]
    public static void Encode_GivenRecordInUnion_WrapsWithFullyQualifiedName()
    {
        const string schemaJson = """
{
  "type": "record",
  "name": "Wrapper",
  "fields": [
    {
      "name": "value",
      "type": ["null", { "type": "record", "name": "Inner", "namespace": "ns", "fields": [ { "name": "x", "type": "int" } ] } ]
    }
  ]
}
""";

        var schema = (RecordSchema)Schema.Parse(schemaJson);
        var innerSchema = (RecordSchema)((UnionSchema)schema.Fields[0].Schema).Schemas[1];
        var inner = new GenericRecord(innerSchema);
        inner.Add("x", 42);
        var record = new GenericRecord(schema);
        record.Add("value", inner);

        var json = AvroJsonWriter.Encode(schema, record);

        Assert.That(json, Is.EqualTo("""{"value":{"ns.Inner":{"x":42}}}"""));
    }

    [Test]
    public static void Encode_GivenEnumInUnion_WrapsWithTypeName()
    {
        const string schemaJson = """
{
  "type": "record",
  "name": "Wrapper",
  "fields": [
    {
      "name": "color",
      "type": ["null", { "type": "enum", "name": "Color", "symbols": ["RED", "GREEN"] } ]
    }
  ]
}
""";

        var schema = (RecordSchema)Schema.Parse(schemaJson);
        var enumSchema = (EnumSchema)((UnionSchema)schema.Fields[0].Schema).Schemas[1];
        var record = new GenericRecord(schema);
        record.Add("color", new GenericEnum(enumSchema, "GREEN"));

        var json = AvroJsonWriter.Encode(schema, record);

        Assert.That(json, Is.EqualTo("""{"color":{"Color":"GREEN"}}"""));
    }

    [Test]
    public static void Encode_GivenFixedInUnion_WrapsWithTypeName()
    {
        const string schemaJson = """
{
  "type": "record",
  "name": "Wrapper",
  "fields": [
    {
      "name": "hash",
      "type": ["null", { "type": "fixed", "name": "Md5", "size": 2 } ]
    }
  ]
}
""";

        var schema = (RecordSchema)Schema.Parse(schemaJson);
        var fixedSchema = (FixedSchema)((UnionSchema)schema.Fields[0].Schema).Schemas[1];
        var record = new GenericRecord(schema);
        record.Add("hash", new GenericFixed(fixedSchema, [0x01, 0x02]));

        var json = AvroJsonWriter.Encode(schema, record);

        // Fixed/bytes values are encoded as a string of raw code points, one per byte;
        // Newtonsoft.Json's writer escapes the resulting control characters as \u-sequences.
        Assert.That(json, Is.EqualTo("{\"hash\":{\"Md5\":\"\\u0001\\u0002\"}}"));
    }

    [Test]
    public static void Encode_GivenNestedRecordAndCollections_EncodesAllFields()
    {
        const string schemaJson = """
{
  "type": "record",
  "name": "Outer",
  "fields": [
    { "name": "tags", "type": { "type": "array", "items": "string" } },
    { "name": "counts", "type": { "type": "map", "values": "int" } },
    { "name": "inner", "type": { "type": "record", "name": "Inner", "fields": [ { "name": "value", "type": "int" } ] } }
  ]
}
""";

        var schema = (RecordSchema)Schema.Parse(schemaJson);
        var innerSchema = (RecordSchema)schema.Fields[2].Schema;
        var inner = new GenericRecord(innerSchema);
        inner.Add("value", 7);

        var record = new GenericRecord(schema);
        record.Add("tags", new[] { "a", "b" });

        var map = new System.Collections.Generic.Dictionary<string, object>
        {
            ["x"] = 1,
        };
        record.Add("counts", map);
        record.Add("inner", inner);

        var json = AvroJsonWriter.Encode(schema, record);

        Assert.That(json, Is.EqualTo("""{"tags":["a","b"],"counts":{"x":1},"inner":{"value":7}}"""));
    }

    [Test]
    public static void Encode_GivenDecimalLogicalType_EncodesUnderlyingBytes()
    {
        const string schemaJson = """
{
  "type": "record",
  "name": "Money",
  "fields": [
    { "name": "amount", "type": { "type": "bytes", "logicalType": "decimal", "precision": 9, "scale": 2 } }
  ]
}
""";

        var schema = (RecordSchema)Schema.Parse(schemaJson);
        var record = new GenericRecord(schema);
        record.Add("amount", new AvroDecimal(123.45m));

        var json = AvroJsonWriter.Encode(schema, record);

        // The unscaled value 12345 as big-endian bytes (0x30, 0x39), encoded as raw code points.
        Assert.That(json, Is.EqualTo("""{"amount":"09"}"""));
    }
}
