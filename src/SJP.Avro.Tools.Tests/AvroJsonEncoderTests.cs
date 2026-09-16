using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Avro;
using Avro.Generic;
using NUnit.Framework;

namespace SJP.Avro.Tools.Tests;

[TestFixture]
internal static class AvroJsonEncoderTests
{
    private const string SchemaJson = """
{
  "type": "record",
  "name": "Person",
  "namespace": "ns",
  "fields": [
    { "name": "name", "type": "string" },
    { "name": "nickname", "type": ["null", "string"], "default": null },
    { "name": "colour", "type": { "type": "enum", "name": "Colour", "symbols": ["Red", "Green"] } },
    { "name": "tags", "type": { "type": "array", "items": "string" } },
    { "name": "counts", "type": { "type": "map", "values": "int" } },
    { "name": "address", "type": ["null", { "type": "record", "name": "Address", "fields": [ { "name": "city", "type": "string" } ] }], "default": null }
  ]
}
""";

    private const string EdgeSchemaJson = """
{
  "type": "record",
  "name": "Edge",
  "namespace": "ns",
  "fields": [
    { "name": "text", "type": "string" },
    { "name": "tags", "type": { "type": "array", "items": "string" } },
    { "name": "counts", "type": { "type": "map", "values": "int" } },
    { "name": "nested", "type": { "type": "array", "items": { "type": "map", "values": "string" } } }
  ]
}
""";

    /// <summary>
    /// Text carrying what one JSON writer is most likely to spell differently from another:
    /// non-ASCII letters, text outside the Latin alphabet, and control characters.
    /// </summary>
    private const string EdgeText = "na\u00efve \u0001 tab\t \u4e16\u754c";

    /// <summary>
    /// <see cref="EdgeText"/> as it appears inside a JSON string: control characters escaped,
    /// everything else written literally, indented or not.
    /// </summary>
    private const string EdgeTextJson = "na\u00efve \\u0001 tab\\t \u4e16\u754c";

    private static RecordSchema Schema => (RecordSchema)global::Avro.Schema.Parse(SchemaJson);

    private static RecordSchema EdgeSchema => (RecordSchema)global::Avro.Schema.Parse(EdgeSchemaJson);

    /// <summary>
    /// A record whose values are the ones a pretty-printer is most likely to differ over: an
    /// empty array, an empty map, a nested collection, and <see cref="EdgeText"/>.
    /// </summary>
    private static GenericRecord CreateEdgeRecord(RecordSchema schema)
    {
        var record = new GenericRecord(schema);
        record.Add("text", EdgeText);
        record.Add("tags", Array.Empty<object>());
        record.Add("counts", new Dictionary<string, object>());
        record.Add("nested", new object[] { new Dictionary<string, object> { ["k"] = "v" } });

        return record;
    }

    private static GenericRecord CreateRecord(RecordSchema schema, int index)
    {
        var colourSchema = (EnumSchema)schema["colour"].Schema;
        var addressSchema = (RecordSchema)((UnionSchema)schema["address"].Schema).Schemas[1];

        var record = new GenericRecord(schema);
        record.Add("name", "Person " + index.ToString(CultureInfo.InvariantCulture));
        record.Add("nickname", index % 2 == 0 ? null : "Nick");
        record.Add("colour", new GenericEnum(colourSchema, index % 2 == 0 ? "Red" : "Green"));
        record.Add("tags", new object[] { "a", "b" });
        record.Add("counts", new Dictionary<string, object> { ["x"] = index });

        if (index % 2 == 0)
        {
            var address = new GenericRecord(addressSchema);
            address.Add("city", "Wellington");
            record.Add("address", address);
        }
        else
        {
            record.Add("address", null);
        }

        return record;
    }

    [Test]
    public static void Ctor_GivenNullSchema_ThrowsArgumentNullException()
    {
        using var writer = new StringWriter();

        Assert.That(() => new AvroJsonEncoder(null!, writer), Throws.ArgumentNullException);
    }

    [Test]
    public static void Ctor_GivenNullOutput_ThrowsArgumentNullException()
    {
        Assert.That(() => new AvroJsonEncoder(Schema, null!), Throws.ArgumentNullException);
    }

    [Test]
    public static void Schema_PropertyGet_ReturnsSchemaProvidedInCtor()
    {
        var schema = Schema;
        using var writer = new StringWriter();

        var encoder = new AvroJsonEncoder(schema, writer);

        Assert.That(encoder.Schema, Is.SameAs(schema));
    }

    [Test]
    public static void Write_GivenManyRecordsThroughOneInstance_MatchesEncodingEachOnItsOwn()
    {
        const int recordCount = 50;

        var schema = Schema;
        var records = new GenericRecord[recordCount];
        for (var i = 0; i < recordCount; i++)
            records[i] = CreateRecord(schema, i);

        var builder = new StringBuilder();
        using var writer = new StringWriter(builder);
        var encoder = new AvroJsonEncoder(schema, writer);

        for (var i = 0; i < recordCount; i++)
        {
            builder.Clear();
            encoder.Write(records[i]);

            Assert.That(builder.ToString(), Is.EqualTo(AvroJsonWriter.Encode(schema, records[i])));
        }
    }

    [Test]
    public static void Write_GivenConsecutiveRecords_WritesThemOneAfterAnotherWithNoSeparator()
    {
        var schema = Schema;
        var first = CreateRecord(schema, 0);
        var second = CreateRecord(schema, 1);

        using var writer = new StringWriter();
        var encoder = new AvroJsonEncoder(schema, writer);

        encoder.Write(first);
        encoder.Write(second);

        var expected = AvroJsonWriter.Encode(schema, first) + AvroJsonWriter.Encode(schema, second);
        Assert.That(writer.ToString(), Is.EqualTo(expected));
    }

    [Test]
    public static void Write_GivenNullDatumForNullSchema_WritesNull()
    {
        using var writer = new StringWriter();
        var encoder = new AvroJsonEncoder(global::Avro.Schema.Parse("\"null\""), writer);

        encoder.Write(null);

        Assert.That(writer.ToString(), Is.EqualTo("null"));
    }

    [Test]
    public static void Write_GivenDatumNotMatchingTheSchema_Throws()
    {
        using var writer = new StringWriter();
        var encoder = new AvroJsonEncoder(global::Avro.Schema.Parse("\"int\""), writer);

        Assert.That(() => encoder.Write("not an int"), Throws.InstanceOf<AvroException>());
    }

    [Test]
    public static void Write_GivenAnyDatum_LeavesTheOutputWriterOpenAndUnflushed()
    {
        var schema = Schema;
        var writer = new CountingTextWriter();

        var encoder = new AvroJsonEncoder(schema, writer);
        encoder.Write(CreateRecord(schema, 0));
        encoder.Write(CreateRecord(schema, 1));

        using (Assert.EnterMultipleScope())
        {
            // A flush per datum would defeat whatever buffering the destination does, which for a
            // container file of any size is what keeps output to one write per block.
            Assert.That(writer.Flushes, Is.Zero);
            Assert.That(writer.Disposals, Is.Zero);
            Assert.That(writer.Text, Is.Not.Empty);
        }
    }

    [Test]
    public static void Write_GivenNoIndent_WritesEdgeCaseValuesOnOneLine()
    {
        var schema = EdgeSchema;

        using var writer = new StringWriter();
        var encoder = new AvroJsonEncoder(schema, writer);

        encoder.Write(CreateEdgeRecord(schema));

        var expected = $$"""{"text":"{{EdgeTextJson}}","tags":[],"counts":{},"nested":[{"k":"v"}]}""";
        Assert.That(writer.ToString(), Is.EqualTo(expected));
    }

    [Test]
    public static void Write_GivenIndent_WritesEdgeCaseValuesOverSeveralLinesIndentedByTwoSpaces()
    {
        var schema = EdgeSchema;

        using var writer = new StringWriter();
        var encoder = new AvroJsonEncoder(schema, writer, indent: true);

        encoder.Write(CreateEdgeRecord(schema));

        // Nothing but whitespace separates this from the one-line form above: the same escaping,
        // the same empty array and empty map, and the same text.
        var expected = string.Join(
            Environment.NewLine,
            "{",
            $"  \"text\": \"{EdgeTextJson}\",",
            "  \"tags\": [],",
            "  \"counts\": {},",
            "  \"nested\": [",
            "    {",
            "      \"k\": \"v\"",
            "    }",
            "  ]",
            "}");
        Assert.That(writer.ToString(), Is.EqualTo(expected));
    }

    [Test]
    public static void Write_GivenIndentAndConsecutiveRecords_WritesThemOneAfterAnotherWithNoSeparator()
    {
        var schema = Schema;
        var first = CreateRecord(schema, 0);
        var second = CreateRecord(schema, 1);

        using var writer = new StringWriter();
        var encoder = new AvroJsonEncoder(schema, writer, indent: true);

        encoder.Write(first);
        encoder.Write(second);

        // A datum ends at its closing brace, with no newline of the writer's own after it, so a
        // caller writing one record per line still decides where each line ends.
        var expected = EncodeIndented(schema, first) + EncodeIndented(schema, second);
        Assert.That(writer.ToString(), Is.EqualTo(expected));
    }

    private static string EncodeIndented(Schema schema, object? datum)
    {
        using var writer = new StringWriter();

        var encoder = new AvroJsonEncoder(schema, writer, indent: true);
        encoder.Write(datum);

        return writer.ToString();
    }

    /// <summary>
    /// A writer that records what was done to it as well as what was written.
    /// </summary>
    private sealed class CountingTextWriter : TextWriter
    {
        private readonly StringBuilder _builder = new();

        public int Flushes { get; private set; }

        public int Disposals { get; private set; }

        public string Text => _builder.ToString();

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value) => _builder.Append(value);

        public override void Flush() => Flushes++;

        protected override void Dispose(bool disposing)
        {
            Disposals++;
            base.Dispose(disposing);
        }
    }
}
