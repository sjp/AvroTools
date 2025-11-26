using System.Linq;
using Avro;
using NUnit.Framework;

namespace SJP.Avro.Tools.Tests;

[TestFixture]
internal static class SchemaExtensionsTests
{
    [Test]
    public static void GetNamedTypes_GivenSimpleRecordSchema_ReturnsSingleRecord()
    {
        const string schemaJson = """
{
  "type": "record",
  "name": "SimpleRecord",
  "namespace": "test.namespace",
  "fields": [
    { "name": "id", "type": "int" },
    { "name": "name", "type": "string" }
  ]
}
""";

        var schema = Schema.Parse(schemaJson);
        var namedTypes = schema.GetNamedTypes().ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(namedTypes, Has.Count.EqualTo(1));
            Assert.That(namedTypes[0], Is.InstanceOf<RecordSchema>());
            Assert.That(namedTypes[0].Name, Is.EqualTo("SimpleRecord"));
        }
    }

    [Test]
    public static void GetNamedTypes_GivenRecordWithNestedRecord_ReturnsBothRecords()
    {
        const string schemaJson = """
{
  "type": "record",
  "name": "OuterRecord",
  "namespace": "test.namespace",
  "fields": [
    { "name": "id", "type": "int" },
    {
      "name": "inner",
      "type": {
        "type": "record",
        "name": "InnerRecord",
        "fields": [
          { "name": "value", "type": "string" }
        ]
      }
    }
  ]
}
""";

        var schema = Schema.Parse(schemaJson);
        var namedTypes = schema.GetNamedTypes().ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(namedTypes, Has.Count.EqualTo(2));
            Assert.That(namedTypes[0].Name, Is.EqualTo("OuterRecord"));
            Assert.That(namedTypes[1].Name, Is.EqualTo("InnerRecord"));
        }
    }

    [Test]
    public static void GetNamedTypes_GivenRecordWithDeeplyNestedRecords_ReturnsAllRecords()
    {
        const string schemaJson = """
{
  "type": "record",
  "name": "TestRecord",
  "namespace": "TestNamespace",
  "fields": [
    {
      "name": "data",
      "type": {
        "type": "array",
        "items": {
          "type": "record",
          "name": "Datum",
          "fields": [
            { "name": "name", "type": ["null", "string"] },
            { "name": "datumId", "type": "int" },
            {
              "name": "pairVolumes",
              "type": {
                "type": "record",
                "name": "PairVolume",
                "fields": [
                  { "name": "negative1", "type": ["null", "double"] },
                  { "name": "negative2", "type": ["null", "double"] }
                ]
              }
            }
          ]
        }
      }
    }
  ]
}
""";

        var schema = Schema.Parse(schemaJson);
        var namedTypes = schema.GetNamedTypes().ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(namedTypes, Has.Count.EqualTo(3));
            Assert.That(namedTypes[0].Name, Is.EqualTo("TestRecord"));
            Assert.That(namedTypes[1].Name, Is.EqualTo("Datum"));
            Assert.That(namedTypes[2].Name, Is.EqualTo("PairVolume"));
        }
    }

    [Test]
    public static void GetNamedTypes_GivenEnumSchema_ReturnsSingleEnum()
    {
        const string schemaJson = """
{
  "type": "enum",
  "name": "Color",
  "namespace": "test.namespace",
  "symbols": ["RED", "GREEN", "BLUE"]
}
""";

        var schema = Schema.Parse(schemaJson);
        var namedTypes = schema.GetNamedTypes().ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(namedTypes, Has.Count.EqualTo(1));
            Assert.That(namedTypes[0], Is.InstanceOf<EnumSchema>());
            Assert.That(namedTypes[0].Name, Is.EqualTo("Color"));
        }
    }

    [Test]
    public static void GetNamedTypes_GivenFixedSchema_ReturnsSingleFixed()
    {
        const string schemaJson = """
{
  "type": "fixed",
  "name": "MD5",
  "namespace": "test.namespace",
  "size": 16
}
""";

        var schema = Schema.Parse(schemaJson);
        var namedTypes = schema.GetNamedTypes().ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(namedTypes, Has.Count.EqualTo(1));
            Assert.That(namedTypes[0], Is.InstanceOf<FixedSchema>());
            Assert.That(namedTypes[0].Name, Is.EqualTo("MD5"));
        }
    }

    [Test]
    public static void GetNamedTypes_GivenRecordWithEnumField_ReturnsBoth()
    {
        const string schemaJson = """
{
  "type": "record",
  "name": "Person",
  "namespace": "test.namespace",
  "fields": [
    { "name": "name", "type": "string" },
    {
      "name": "favoriteColor",
      "type": {
        "type": "enum",
        "name": "Color",
        "symbols": ["RED", "GREEN", "BLUE"]
      }
    }
  ]
}
""";

        var schema = Schema.Parse(schemaJson);
        var namedTypes = schema.GetNamedTypes().ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(namedTypes, Has.Count.EqualTo(2));
            Assert.That(namedTypes[0].Name, Is.EqualTo("Person"));
            Assert.That(namedTypes[1].Name, Is.EqualTo("Color"));
        }
    }

    [Test]
    public static void GetNamedTypes_GivenRecordWithFixedField_ReturnsBoth()
    {
        const string schemaJson = """
{
  "type": "record",
  "name": "Document",
  "namespace": "test.namespace",
  "fields": [
    { "name": "content", "type": "string" },
    {
      "name": "hash",
      "type": {
        "type": "fixed",
        "name": "MD5",
        "size": 16
      }
    }
  ]
}
""";

        var schema = Schema.Parse(schemaJson);
        var namedTypes = schema.GetNamedTypes().ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(namedTypes, Has.Count.EqualTo(2));
            Assert.That(namedTypes[0].Name, Is.EqualTo("Document"));
            Assert.That(namedTypes[1].Name, Is.EqualTo("MD5"));
        }
    }

    [Test]
    public static void GetNamedTypes_GivenArrayOfRecords_ReturnsRecords()
    {
        const string schemaJson = """
{
  "type": "array",
  "items": {
    "type": "record",
    "name": "Item",
    "namespace": "test.namespace",
    "fields": [
      { "name": "value", "type": "int" }
    ]
  }
}
""";

        var schema = Schema.Parse(schemaJson);
        var namedTypes = schema.GetNamedTypes().ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(namedTypes, Has.Count.EqualTo(1));
            Assert.That(namedTypes[0].Name, Is.EqualTo("Item"));
        }
    }

    [Test]
    public static void GetNamedTypes_GivenMapOfRecords_ReturnsRecords()
    {
        const string schemaJson = """
{
  "type": "map",
  "values": {
    "type": "record",
    "name": "Value",
    "namespace": "test.namespace",
    "fields": [
      { "name": "data", "type": "string" }
    ]
  }
}
""";

        var schema = Schema.Parse(schemaJson);
        var namedTypes = schema.GetNamedTypes().ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(namedTypes, Has.Count.EqualTo(1));
            Assert.That(namedTypes[0].Name, Is.EqualTo("Value"));
        }
    }

    [Test]
    public static void GetNamedTypes_GivenUnionWithMultipleRecords_ReturnsAllRecords()
    {
        const string schemaJson = """
{
  "type": "record",
  "name": "Container",
  "namespace": "test.namespace",
  "fields": [
    {
      "name": "item",
      "type": [
        "null",
        {
          "type": "record",
          "name": "TypeA",
          "fields": [
            { "name": "a", "type": "int" }
          ]
        },
        {
          "type": "record",
          "name": "TypeB",
          "fields": [
            { "name": "b", "type": "string" }
          ]
        }
      ]
    }
  ]
}
""";

        var schema = Schema.Parse(schemaJson);
        var namedTypes = schema.GetNamedTypes().ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(namedTypes, Has.Count.EqualTo(3));
            Assert.That(namedTypes[0].Name, Is.EqualTo("Container"));
            Assert.That(namedTypes[1].Name, Is.EqualTo("TypeA"));
            Assert.That(namedTypes[2].Name, Is.EqualTo("TypeB"));
        }
    }

    [Test]
    public static void GetNamedTypes_GivenPrimitiveType_ReturnsEmpty()
    {
        var schema = Schema.Parse("\"int\"");
        var namedTypes = schema.GetNamedTypes().ToList();

        Assert.That(namedTypes, Is.Empty);
    }

    [Test]
    public static void GetNamedTypes_GivenArrayOfPrimitives_ReturnsEmpty()
    {
        const string schemaJson = """
{
  "type": "array",
  "items": "string"
}
""";

        var schema = Schema.Parse(schemaJson);
        var namedTypes = schema.GetNamedTypes().ToList();

        Assert.That(namedTypes, Is.Empty);
    }

    [Test]
    public static void GetNamedTypes_GivenMapOfPrimitives_ReturnsEmpty()
    {
        const string schemaJson = """
{
  "type": "map",
  "values": "int"
}
""";

        var schema = Schema.Parse(schemaJson);
        var namedTypes = schema.GetNamedTypes().ToList();

        Assert.That(namedTypes, Is.Empty);
    }

    [Test]
    public static void GetNamedTypes_GivenDuplicateTypesInDifferentFields_ReturnsUniqueTypes()
    {
        const string schemaJson = """
{
  "type": "record",
  "name": "Container",
  "namespace": "test.namespace",
  "fields": [
    {
      "name": "field1",
      "type": {
        "type": "record",
        "name": "Shared",
        "fields": [
          { "name": "value", "type": "int" }
        ]
      }
    },
    {
      "name": "field2",
      "type": "Shared"
    }
  ]
}
""";

        var schema = Schema.Parse(schemaJson);
        var namedTypes = schema.GetNamedTypes().ToList();

        // Should return only Container and Shared, not Shared twice
        using (Assert.EnterMultipleScope())
        {
            Assert.That(namedTypes, Has.Count.EqualTo(2));
            Assert.That(namedTypes[0].Name, Is.EqualTo("Container"));
            Assert.That(namedTypes[1].Name, Is.EqualTo("Shared"));
        }
    }

    [Test]
    public static void GetNamedTypes_GivenComplexMixedTypes_ReturnsAllNamedTypes()
    {
        const string schemaJson = """
{
  "type": "record",
  "name": "ComplexRecord",
  "namespace": "test.namespace",
  "fields": [
    {
      "name": "enumField",
      "type": {
        "type": "enum",
        "name": "Status",
        "symbols": ["ACTIVE", "INACTIVE"]
      }
    },
    {
      "name": "fixedField",
      "type": {
        "type": "fixed",
        "name": "UUID",
        "size": 16
      }
    },
    {
      "name": "arrayField",
      "type": {
        "type": "array",
        "items": {
          "type": "record",
          "name": "ArrayItem",
          "fields": [
            { "name": "id", "type": "int" }
          ]
        }
      }
    },
    {
      "name": "mapField",
      "type": {
        "type": "map",
        "values": {
          "type": "record",
          "name": "MapValue",
          "fields": [
            { "name": "data", "type": "string" }
          ]
        }
      }
    }
  ]
}
""";

        var schema = Schema.Parse(schemaJson);
        var namedTypes = schema.GetNamedTypes().ToList();

        var expectedNamedTypes = new[]
        {
            "ComplexRecord",
            "Status",
            "UUID",
            "ArrayItem",
            "MapValue"
        };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(namedTypes, Has.Count.EqualTo(5));
            Assert.That(namedTypes.Select(t => t.Name), Is.EquivalentTo(expectedNamedTypes));
        }
    }

    [Test]
    public static void GetNamedTypes_GivenRecordWithSelfReference_DoesNotInfiniteLoop()
    {
        const string schemaJson = """
{
  "type": "record",
  "name": "LinkedNode",
  "namespace": "test.namespace",
  "fields": [
    { "name": "value", "type": "int" },
    { "name": "next", "type": ["null", "LinkedNode"] }
  ]
}
""";

        var schema = Schema.Parse(schemaJson);
        var namedTypes = schema.GetNamedTypes().ToList();

        // Should return only one LinkedNode, not infinite
        using (Assert.EnterMultipleScope())
        {
            Assert.That(namedTypes, Has.Count.EqualTo(1));
            Assert.That(namedTypes[0].Name, Is.EqualTo("LinkedNode"));
        }
    }

    [Test]
    public static void GetNamedTypes_GivenRecordWithCircularReference_DoesNotInfiniteLoop()
    {
        const string schemaJson = """
{
  "type": "record",
  "name": "NodeA",
  "namespace": "test.namespace",
  "fields": [
    { "name": "value", "type": "int" },
    {
      "name": "nodeB",
      "type": {
        "type": "record",
        "name": "NodeB",
        "fields": [
          { "name": "data", "type": "string" },
          { "name": "nodeA", "type": ["null", "NodeA"] }
        ]
      }
    }
  ]
}
""";

        var schema = Schema.Parse(schemaJson);
        var namedTypes = schema.GetNamedTypes().ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(namedTypes, Has.Count.EqualTo(2));
            Assert.That(namedTypes[0].Name, Is.EqualTo("NodeA"));
            Assert.That(namedTypes[1].Name, Is.EqualTo("NodeB"));
        }
    }

    [Test]
    public static void GetNamedTypes_GivenNamespacedTypes_PreservesFullNames()
    {
        const string schemaJson = """
{
  "type": "record",
  "name": "Record",
  "namespace": "com.example",
  "fields": [
    {
      "name": "field",
      "type": {
        "type": "record",
        "name": "Nested",
        "namespace": "com.example.nested",
        "fields": [
          { "name": "value", "type": "int" }
        ]
      }
    }
  ]
}
""";

        var schema = Schema.Parse(schemaJson);
        var namedTypes = schema.GetNamedTypes().ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(namedTypes, Has.Count.EqualTo(2));
            Assert.That(namedTypes[0].Fullname, Is.EqualTo("com.example.Record"));
            Assert.That(namedTypes[1].Fullname, Is.EqualTo("com.example.nested.Nested"));
        }
    }
}
