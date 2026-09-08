using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SJP.Avro.Tools.Idl;

namespace SJP.Avro.Tools.Tests.Idl;

[TestFixture]
internal class IdlJsonTests
{
    [Test]
    public void GetFullName_GivenNameWithoutNamespace_ReturnsNameUnchanged()
    {
        var schema = JObject.Parse("""{ "type": "record", "name": "R", "fields": [] }""");

        Assert.That(IdlJson.GetFullName(schema), Is.EqualTo("R"));
    }

    [Test]
    public void GetFullName_GivenNameAndNamespace_ReturnsQualifiedName()
    {
        var schema = JObject.Parse("""{ "type": "record", "name": "R", "namespace": "com.example", "fields": [] }""");

        Assert.That(IdlJson.GetFullName(schema), Is.EqualTo("com.example.R"));
    }

    [Test]
    public void GetFullName_GivenAlreadyDottedName_ReturnsNameUnchanged()
    {
        var schema = JObject.Parse("""{ "type": "record", "name": "com.example.R", "namespace": "ignored", "fields": [] }""");

        Assert.That(IdlJson.GetFullName(schema), Is.EqualTo("com.example.R"));
    }

    [Test]
    public void GetNamedTypes_GivenRecordReferencingAnotherRecordInTheSameNamespace_FindsBoth()
    {
        var referenced = JObject.Parse("""{ "type": "record", "name": "Inner", "namespace": "com.example", "fields": [] }""");
        var root = JObject.Parse("""
            {
              "type": "record",
              "name": "Outer",
              "namespace": "com.example",
              "fields": [ { "name": "inner", "type": "Inner" } ]
            }
            """);

        var namedSchemas = new Dictionary<string, JObject>
        {
            ["com.example.Outer"] = root,
            ["com.example.Inner"] = referenced,
        };

        var found = IdlJson.GetNamedTypes(root, namedSchemas);

        Assert.That(found, Has.Count.EqualTo(2));
        Assert.That(IdlJson.GetFullName(found[0]), Is.EqualTo("com.example.Outer"));
        Assert.That(IdlJson.GetFullName(found[1]), Is.EqualTo("com.example.Inner"));
    }

    [Test]
    public void GetNamedTypes_GivenTypeReachedThroughAnArrayAndAUnion_FindsIt()
    {
        var referenced = JObject.Parse("""{ "type": "enum", "name": "Suit", "symbols": [ "H", "S" ] }""");
        var root = JObject.Parse("""
            {
              "type": "record",
              "name": "Hand",
              "fields": [
                { "name": "cards", "type": { "type": "array", "items": [ "null", "Suit" ] } }
              ]
            }
            """);

        var namedSchemas = new Dictionary<string, JObject> { ["Suit"] = referenced };

        var found = IdlJson.GetNamedTypes(root, namedSchemas);

        Assert.That(found.Count, Is.EqualTo(2));
        Assert.That(IdlJson.GetFullName(found[1]), Is.EqualTo("Suit"));
    }

    [Test]
    public void Inline_GivenSelfReferentialRecord_LeavesTheSelfReferenceAsABareName()
    {
        var root = JObject.Parse("""
            {
              "type": "record",
              "name": "Node",
              "namespace": "com.example",
              "fields": [ { "name": "next", "type": [ "null", "Node" ] } ]
            }
            """);

        var namedSchemas = new Dictionary<string, JObject> { ["com.example.Node"] = root };

        var inlined = IdlJson.Inline(root, namedSchemas);

        var nextType = (JArray)inlined["fields"]![0]!["type"]!;
        Assert.That(nextType[1].Type, Is.EqualTo(JTokenType.String));
        Assert.That(nextType[1].Value<string>(), Is.EqualTo("Node"));
    }

    [Test]
    public void Inline_GivenAnUnqualifiedReferenceInTheSameNamespace_ResolvesItAndInlinesTheDefinition()
    {
        var referenced = JObject.Parse("""{ "type": "enum", "name": "Kind", "namespace": "com.example", "symbols": [ "A", "B" ] }""");
        var root = JObject.Parse("""
            {
              "type": "record",
              "name": "R",
              "namespace": "com.example",
              "fields": [ { "name": "kind", "type": "Kind" } ]
            }
            """);

        var namedSchemas = new Dictionary<string, JObject>
        {
            ["com.example.R"] = root,
            ["com.example.Kind"] = referenced,
        };

        var inlined = IdlJson.Inline(root, namedSchemas);

        var kindType = inlined["fields"]![0]!["type"];
        Assert.That(kindType!.Type, Is.EqualTo(JTokenType.Object));
        Assert.That(kindType.Value<string>("name"), Is.EqualTo("Kind"));
        Assert.That(kindType.Value<string>("namespace"), Is.EqualTo("com.example"));
    }

    [Test]
    public void Inline_GivenMutuallyRecursiveTypes_TerminatesAndLeavesTheCycleAsABareReference()
    {
        var b = JObject.Parse("""
            {
              "type": "record",
              "name": "B",
              "namespace": "com.example",
              "fields": [ { "name": "a", "type": "A" } ]
            }
            """);
        var a = JObject.Parse("""
            {
              "type": "record",
              "name": "A",
              "namespace": "com.example",
              "fields": [ { "name": "b", "type": "B" } ]
            }
            """);

        var namedSchemas = new Dictionary<string, JObject>
        {
            ["com.example.A"] = a,
            ["com.example.B"] = b,
        };

        var inlined = IdlJson.Inline(a, namedSchemas);

        var bType = inlined["fields"]![0]!["type"];
        Assert.That(bType!.Type, Is.EqualTo(JTokenType.Object));
        Assert.That(bType.Value<string>("name"), Is.EqualTo("B"));

        var aType = bType["fields"]![0]!["type"];
        Assert.That(aType!.Type, Is.EqualTo(JTokenType.String));
        Assert.That(aType.Value<string>(), Is.EqualTo("A"));
    }
}
