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
}
