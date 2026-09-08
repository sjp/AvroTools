using System;
using Avro;
using NUnit.Framework;

namespace SJP.Avro.Tools.CodeGen.Tests;

[TestFixture]
internal static class AvroSchemaUtilitiesTests
{
    [TestCase(""" [ "null", "int" ] """)]
    [TestCase(""" [ "null", "string" ] """)]
    [TestCase(""" [ "string", "null" ] """)]
    [TestCase(""" [ "null", "int", "string" ] """)]
    [TestCase(""" [ "null" ] """)]
    public static void IsNullable_GivenUnionContainingNull_ReturnsTrue(string json)
    {
        var schema = Schema.Parse(json);

        Assert.That(AvroSchemaUtilities.IsNullable(schema), Is.True);
    }

    [TestCase(""" "int" """)]
    [TestCase(""" "string" """)]
    [TestCase(""" [ "int", "string" ] """)]
    [TestCase(""" { "type" : "array", "items" : "string" } """)]
    [TestCase(""" { "type" : "record", "name" : "A", "fields" : [] } """)]
    public static void IsNullable_GivenSchemaWithoutANullBranch_ReturnsFalse(string json)
    {
        var schema = Schema.Parse(json);

        Assert.That(AvroSchemaUtilities.IsNullable(schema), Is.False);
    }

    [TestCase(""" "boolean" """)]
    [TestCase(""" "int" """)]
    [TestCase(""" "long" """)]
    [TestCase(""" "float" """)]
    [TestCase(""" "double" """)]
    [TestCase(""" { "type" : "enum", "name" : "E", "symbols" : [ "X" ] } """)]
    [TestCase(""" { "type" : "int", "logicalType" : "date" } """)]
    [TestCase(""" { "type" : "string", "logicalType" : "uuid" } """)]
    [TestCase(""" { "type" : "bytes", "logicalType" : "decimal", "precision" : 4, "scale" : 2 } """)]
    // A logical type Avro does not implement takes the answer from the type backing it.
    [TestCase(""" { "type" : "long", "logicalType" : "made-up" } """)]
    public static void IsValueType_GivenSchemaMappedOntoAValueType_ReturnsTrue(string json)
    {
        var schema = Schema.Parse(json);

        Assert.That(AvroSchemaUtilities.IsValueType(schema), Is.True);
    }

    [TestCase(""" "null" """)]
    [TestCase(""" "string" """)]
    [TestCase(""" "bytes" """)]
    [TestCase(""" { "type" : "array", "items" : "int" } """)]
    [TestCase(""" { "type" : "map", "values" : "int" } """)]
    [TestCase(""" { "type" : "fixed", "name" : "F", "size" : 4 } """)]
    [TestCase(""" { "type" : "record", "name" : "A", "fields" : [] } """)]
    [TestCase(""" { "type" : "fixed", "name" : "D", "size" : 12, "logicalType" : "duration" } """)]
    [TestCase(""" { "type" : "string", "logicalType" : "made-up" } """)]
    public static void IsValueType_GivenSchemaMappedOntoAReferenceType_ReturnsFalse(string json)
    {
        var schema = Schema.Parse(json);

        Assert.That(AvroSchemaUtilities.IsValueType(schema), Is.False);
    }

    // A union is never itself a value type, however its branches are typed: the generated property
    // is nullable, and a union of several branches is typed as object.
    [TestCase(""" [ "null", "int" ] """)]
    [TestCase(""" [ "null", "string" ] """)]
    [TestCase(""" [ "int", "string" ] """)]
    public static void IsValueType_GivenUnion_ReturnsFalse(string json)
    {
        var schema = Schema.Parse(json);

        Assert.That(AvroSchemaUtilities.IsValueType(schema), Is.False);
    }

    [TestCase(""" "boolean" """, "bool")]
    [TestCase(""" "int" """, "int")]
    [TestCase(""" "long" """, "long")]
    [TestCase(""" "float" """, "float")]
    [TestCase(""" "double" """, "double")]
    [TestCase(""" "string" """, "string")]
    [TestCase(""" "bytes" """, "byte[]")]
    [TestCase(""" "null" """, "object")]
    [TestCase(""" { "type" : "enum", "name" : "E", "symbols" : [ "X" ] } """, "E")]
    [TestCase(""" { "type" : "fixed", "name" : "F", "size" : 4 } """, "F")]
    [TestCase(""" { "type" : "record", "name" : "A", "fields" : [] } """, "A")]
    [TestCase(""" { "type" : "error", "name" : "Oops", "fields" : [] } """, "Oops")]
    [TestCase(""" { "type" : "array", "items" : "string" } """, "List<string>")]
    [TestCase(""" { "type" : "map", "values" : "int" } """, "IDictionary<string,int>")]
    [TestCase(""" [ "null", "int" ] """, "int?")]
    [TestCase(""" [ "null", "string" ] """, "string?")]
    [TestCase(""" [ "int", "string" ] """, "object")]
    [TestCase(""" [ "null", "int", "string" ] """, "object?")]
    [TestCase(""" { "type" : "string", "logicalType" : "uuid" } """, "Guid")]
    [TestCase(""" { "type" : "int", "logicalType" : "date" } """, "DateTime")]
    [TestCase(""" { "type" : "int", "logicalType" : "time-millis" } """, "TimeSpan")]
    [TestCase(""" { "type" : "long", "logicalType" : "time-micros" } """, "TimeSpan")]
    [TestCase(""" { "type" : "long", "logicalType" : "timestamp-millis" } """, "DateTime")]
    [TestCase(""" { "type" : "long", "logicalType" : "timestamp-micros" } """, "DateTime")]
    [TestCase(""" { "type" : "long", "logicalType" : "local-timestamp-millis" } """, "DateTime")]
    [TestCase(""" { "type" : "long", "logicalType" : "local-timestamp-micros" } """, "DateTime")]
    [TestCase(""" { "type" : "bytes", "logicalType" : "decimal", "precision" : 4, "scale" : 2 } """, "decimal")]
    [TestCase(""" [ "null", { "type" : "bytes", "logicalType" : "decimal", "precision" : 4, "scale" : 2 } ] """, "decimal?")]
    public static void GetFieldType_GivenSchema_ReturnsDocumentedCsharpType(string json, string expectedType)
    {
        var schema = Schema.Parse(json);

        Assert.That(AvroSchemaUtilities.GetFieldType(schema).ToFullString(), Is.EqualTo(expectedType));
    }

    // A decimal reached through a collection keeps the representation Avro hands to and
    // from the collection's elements, so it is not converted to 'decimal'.
    [TestCase(""" { "type" : "array", "items" : { "type" : "bytes", "logicalType" : "decimal", "precision" : 4, "scale" : 2 } } """, "List<AvroDecimal>")]
    [TestCase(""" { "type" : "map", "values" : { "type" : "bytes", "logicalType" : "decimal", "precision" : 4, "scale" : 2 } } """, "IDictionary<string,AvroDecimal>")]
    public static void GetFieldType_GivenCollectionOfDecimals_ReturnsUnconvertedElementType(string json, string expectedType)
    {
        var schema = Schema.Parse(json);

        Assert.That(AvroSchemaUtilities.GetFieldType(schema).ToFullString(), Is.EqualTo(expectedType));
    }

    // Avro exchanges a decimal stored in a fixed as a generic fixed, which its specific writer and
    // reader both refuse, so there is no member type that could carry the value.
    [TestCase(""" { "type" : "fixed", "name" : "M", "size" : 8, "logicalType" : "decimal", "precision" : 10, "scale" : 2 } """)]
    [TestCase(""" [ "null", { "type" : "fixed", "name" : "M", "size" : 8, "logicalType" : "decimal", "precision" : 10, "scale" : 2 } ] """)]
    [TestCase(""" { "type" : "array", "items" : { "type" : "fixed", "name" : "M", "size" : 8, "logicalType" : "decimal", "precision" : 10, "scale" : 2 } } """)]
    [TestCase(""" { "type" : "map", "values" : { "type" : "fixed", "name" : "M", "size" : 8, "logicalType" : "decimal", "precision" : 10, "scale" : 2 } } """)]
    public static void GetFieldType_GivenFixedBackedDecimal_ThrowsNotSupported(string json)
    {
        var schema = Schema.Parse(json);

        var exception = Assert.Throws<NotSupportedException>(() => AvroSchemaUtilities.GetFieldType(schema));

        Assert.That(exception!.Message, Does.Contain("M"));
    }

    // Avro implements a fixed set of logical types and hands every other one through as the type
    // backing it, so that backing type is what the generated member has to be typed as. 'duration'
    // is one of those: the specification defines it, but the library has no conversion for it.
    [TestCase(""" { "type" : "fixed", "name" : "D", "size" : 12, "logicalType" : "duration" } """, "D")]
    [TestCase(""" { "type" : "long", "logicalType" : "made-up" } """, "long")]
    [TestCase(""" { "type" : "string", "logicalType" : "made-up" } """, "string")]
    [TestCase(""" [ "null", { "type" : "fixed", "name" : "D", "size" : 12, "logicalType" : "duration" } ] """, "D?")]
    [TestCase(""" { "type" : "array", "items" : { "type" : "fixed", "name" : "D", "size" : 12, "logicalType" : "duration" } } """, "List<D>")]
    public static void GetFieldType_GivenLogicalTypeAvroDoesNotImplement_ReturnsTypeOfBackingSchema(string json, string expectedType)
    {
        var schema = Schema.Parse(json);

        Assert.That(AvroSchemaUtilities.GetFieldType(schema).ToFullString(), Is.EqualTo(expectedType));
    }

    // Avro writes a logical type over a named type as a wrapper around it. The specification puts
    // the logical attributes on the type itself, which is the only form other implementations parse.
    [Test]
    public static void ToPortableJson_GivenLogicalTypeBackedByNamedType_MovesAttributesOntoTheType()
    {
        const string json = """{"type":"record","name":"A","namespace":"n","fields":[{"name":"f","type":{"type":{"type":"fixed","name":"D","namespace":"n","size":12},"logicalType":"duration"}}]}""";

        Assert.That(
            AvroSchemaUtilities.ToPortableJson(json),
            Is.EqualTo("""{"type":"record","name":"A","namespace":"n","fields":[{"name":"f","type":{"type":"fixed","name":"D","namespace":"n","size":12,"logicalType":"duration"}}]}"""));
    }

    [Test]
    public static void ToPortableJson_GivenLogicalTypeBackedByNamedTypeInsideAUnion_MovesAttributesOntoTheType()
    {
        const string json = """{"type":"record","name":"A","namespace":"n","fields":[{"name":"f","type":["null",{"type":{"type":"fixed","name":"D","namespace":"n","size":12},"logicalType":"duration"}]}]}""";

        Assert.That(
            AvroSchemaUtilities.ToPortableJson(json),
            Is.EqualTo("""{"type":"record","name":"A","namespace":"n","fields":[{"name":"f","type":["null",{"type":"fixed","name":"D","namespace":"n","size":12,"logicalType":"duration"}]}]}"""));
    }

    // A logical type over a primitive is already written in its portable form, and a field of its
    // own may carry a 'logicalType' attribute without being a wrapper, so both are left untouched.
    [TestCase("""{"type":"record","name":"A","namespace":"n","fields":[{"name":"f","type":{"type":"bytes","logicalType":"decimal","precision":10,"scale":2}}]}""")]
    [TestCase("""{"type":"record","name":"A","namespace":"n","fields":[{"name":"f","type":{"type":"long","logicalType":"my-thing"}}]}""")]
    [TestCase("""{"type":"record","name":"A","namespace":"n","fields":[{"name":"f","type":{"type":"record","name":"B","fields":[]},"logicalType":"annotated"}]}""")]
    public static void ToPortableJson_GivenSchemaWithoutAWrappedLogicalType_ReturnsItUnchanged(string json)
    {
        Assert.That(AvroSchemaUtilities.ToPortableJson(json), Is.EqualTo(json));
    }

    [Test]
    public static void ToPortableJson_GivenRewrittenSchema_LeavesTextOutsideAsciiUnescaped()
    {
        const string json = """{"type":"record","name":"A","namespace":"n","doc":"caf\u00e9 & co","fields":[{"name":"f","type":{"type":{"type":"fixed","name":"D","namespace":"n","size":12},"logicalType":"duration"}}]}""";

        Assert.That(
            AvroSchemaUtilities.ToPortableJson(json),
            Is.EqualTo("""{"type":"record","name":"A","namespace":"n","doc":"café & co","fields":[{"name":"f","type":{"type":"fixed","name":"D","namespace":"n","size":12,"logicalType":"duration"}}]}"""));
    }
}
