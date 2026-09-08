using System;
using System.Collections.Generic;
using Avro;
using Avro.Generic;
using Avro.Util;
using NUnit.Framework;

namespace SJP.Avro.Tools.Tests;

[TestFixture]
internal static class AvroJsonWriterTests
{
    [Test]
    public static void Encode_GivenNullSchema_ThrowsArgumentNullException()
    {
        Assert.That(() => AvroJsonWriter.Encode(null!, 1), Throws.ArgumentNullException);
    }

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

        var map = new Dictionary<string, object>
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

    [Test]
    public static void Encode_GivenLogicalTypeInUnion_WrapsWithTheBaseTypeName()
    {
        const string schemaJson = """
{
  "type": "record",
  "name": "Event",
  "fields": [
    { "name": "at", "type": ["null", { "type": "long", "logicalType": "timestamp-millis" }], "default": null }
  ]
}
""";

        var schema = (RecordSchema)Schema.Parse(schemaJson);
        var record = new GenericRecord(schema);
        record.Add("at", new DateTime(1970, 1, 1, 0, 0, 1, 234, DateTimeKind.Utc));

        var json = AvroJsonWriter.Encode(schema, record);

        // A union branch is labelled with the name of the type written on the wire. A logical type
        // is written as its underlying representation, so the label is that base type's name.
        Assert.That(json, Is.EqualTo("""{"at":{"long":1234}}"""));
    }

    [Test]
    public static void Encode_GivenLongBeyondDoublePrecision_WritesItUnquotedAndExact()
    {
        const string schemaJson = """
{
  "type": "record",
  "name": "Ids",
  "fields": [
    { "name": "id", "type": "long" }
  ]
}
""";

        var schema = (RecordSchema)Schema.Parse(schemaJson);
        var record = new GenericRecord(schema);
        record.Add("id", 9007199254740993L);

        var json = AvroJsonWriter.Encode(schema, record);

        Assert.That(json, Is.EqualTo("""{"id":9007199254740993}"""));
    }

    // JSON has no literal for a non-finite number, so the Avro JSON encoding writes these as the
    // quoted names the specification gives them.
    [TestCase(double.NaN, "NaN")]
    [TestCase(double.PositiveInfinity, "Infinity")]
    [TestCase(double.NegativeInfinity, "-Infinity")]
    public static void Encode_GivenNonFiniteDouble_WritesItAsAQuotedString(double value, string expected)
    {
        const string schemaJson = """
{
  "type": "record",
  "name": "Measurement",
  "fields": [
    { "name": "value", "type": "double" }
  ]
}
""";

        var schema = (RecordSchema)Schema.Parse(schemaJson);
        var record = new GenericRecord(schema);
        record.Add("value", value);

        var json = AvroJsonWriter.Encode(schema, record);

        Assert.That(json, Is.EqualTo($$"""{"value":"{{expected}}"}"""));
    }

    [Test]
    public static void Encode_GivenBytesOutsideAscii_MapsEachByteToTheCodePointOfTheSameValue()
    {
        const string schemaJson = """
{
  "type": "record",
  "name": "Blob",
  "fields": [
    { "name": "data", "type": "bytes" }
  ]
}
""";

        var schema = (RecordSchema)Schema.Parse(schemaJson);
        var record = new GenericRecord(schema);
        record.Add("data", new byte[] { 0x80, 0xC3, 0xFF });

        var json = AvroJsonWriter.Encode(schema, record);

        // Each byte becomes the code point of the same value, so a byte at or above 0x80 becomes a
        // Latin-1 character rather than being interpreted as part of a UTF-8 sequence.
        Assert.That(json, Is.EqualTo("{\"data\":\"\u0080\u00C3\u00FF\"}"));
    }

    [Test]
    public static void Encode_GivenBytesThatMapToJsonMetacharacters_EscapesThem()
    {
        const string schemaJson = """
{
  "type": "record",
  "name": "Blob",
  "fields": [
    { "name": "data", "type": "bytes" }
  ]
}
""";

        var schema = (RecordSchema)Schema.Parse(schemaJson);
        var record = new GenericRecord(schema);
        record.Add("data", new byte[] { 0x22, 0x5C });

        var json = AvroJsonWriter.Encode(schema, record);

        Assert.That(json, Is.EqualTo("{\"data\":\"\\\"\\\\\"}"));
    }

    [Test]
    public static void Encode_GivenFixedWithBytesOutsideAscii_MapsEachByteToTheCodePointOfTheSameValue()
    {
        const string schemaJson = """
{
  "type": "record",
  "name": "Wrapper",
  "fields": [
    { "name": "hash", "type": { "type": "fixed", "name": "Pair", "size": 2 } }
  ]
}
""";

        var schema = (RecordSchema)Schema.Parse(schemaJson);
        var fixedSchema = (FixedSchema)schema.Fields[0].Schema;
        var record = new GenericRecord(schema);
        record.Add("hash", new GenericFixed(fixedSchema, [0x80, 0xFF]));

        var json = AvroJsonWriter.Encode(schema, record);

        Assert.That(json, Is.EqualTo("{\"hash\":\"\u0080\u00FF\"}"));
    }

    [Test]
    public static void Encode_GivenNullDatumForNullSchema_WritesNull()
    {
        var schema = Schema.Parse("\"null\"");

        var json = AvroJsonWriter.Encode(schema, null);

        Assert.That(json, Is.EqualTo("null"));
    }

    [Test]
    public static void Encode_GivenNullDatumForUnionWithNullBranch_WritesNull()
    {
        var schema = Schema.Parse("""["null","string"]""");

        var json = AvroJsonWriter.Encode(schema, null);

        Assert.That(json, Is.EqualTo("null"));
    }

    [Test]
    public static void Encode_GivenMapOfUnions_WrapsEachNonNullValueWithItsTypeName()
    {
        const string schemaJson = """
{
  "type": "record",
  "name": "Attributes",
  "fields": [
    { "name": "values", "type": { "type": "map", "values": ["null", "int", "string"] } }
  ]
}
""";

        var schema = (RecordSchema)Schema.Parse(schemaJson);
        var record = new GenericRecord(schema);
        record.Add("values", new Dictionary<string, object>
        {
            ["a"] = 1,
            ["b"] = "two",
            ["c"] = null,
        });

        var json = AvroJsonWriter.Encode(schema, record);

        Assert.That(json, Is.EqualTo("""{"values":{"a":{"int":1},"b":{"string":"two"},"c":null}}"""));
    }
}
