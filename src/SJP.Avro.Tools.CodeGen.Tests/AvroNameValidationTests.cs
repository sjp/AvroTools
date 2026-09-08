using Avro;
using NUnit.Framework;

namespace SJP.Avro.Tools.CodeGen.Tests;

[TestFixture]
internal static class AvroNameValidationTests
{
    [Test]
    public static void FindUnusableNames_GivenNullProtocol_ThrowsArgumentNullException()
    {
        Assert.That(() => AvroNameValidation.FindUnusableNames(default(Protocol)!), Throws.ArgumentNullException);
    }

    [Test]
    public static void FindUnusableNames_GivenNullSchema_ThrowsArgumentNullException()
    {
        Assert.That(() => AvroNameValidation.FindUnusableNames(default(NamedSchema)!), Throws.ArgumentNullException);
    }

    [Test]
    public static void FindUnusableNames_GivenUsableProtocolNames_ReturnsNothing()
    {
        var protocol = Protocol.Parse("""
{
  "protocol" : "Service",
  "namespace" : "some.name.space",
  "types" : [ ],
  "messages" : { "go" : { "request" : [ ], "response" : "null" } }
}
""");

        Assert.That(AvroNameValidation.FindUnusableNames(protocol), Is.Empty);
    }

    [Test]
    public static void FindUnusableNames_GivenProtocolNamespaceCsharpCannotSpell_ReturnsTheNamespace()
    {
        var protocol = Protocol.Parse("""
{
  "protocol" : "Service",
  "namespace" : "my-ns.inner",
  "types" : [ ],
  "messages" : { "go" : { "request" : [ ], "response" : "null" } }
}
""");

        Assert.That(AvroNameValidation.FindUnusableNames(protocol), Is.EqualTo(new[] { "my-ns.inner" }));
    }

    [Test]
    public static void FindUnusableNames_GivenProtocolNameCsharpCannotSpell_ReturnsTheName()
    {
        var protocol = Protocol.Parse("""
{
  "protocol" : "my-service",
  "namespace" : "some.name.space",
  "types" : [ ],
  "messages" : { "go" : { "request" : [ ], "response" : "null" } }
}
""");

        Assert.That(AvroNameValidation.FindUnusableNames(protocol), Is.EqualTo(new[] { "my-service" }));
    }

    [Test]
    public static void FindUnusableNames_GivenProtocolWithNoNamespace_ReturnsNothing()
    {
        var protocol = Protocol.Parse("""
{
  "protocol" : "Service",
  "types" : [ ],
  "messages" : { "go" : { "request" : [ ], "response" : "null" } }
}
""");

        Assert.That(AvroNameValidation.FindUnusableNames(protocol), Is.Empty);
    }

    [Test]
    public static void FindUnusableNames_GivenProtocolNameThatIsAKeyword_ReturnsNothing()
    {
        var protocol = Protocol.Parse("""
{
  "protocol" : "class",
  "namespace" : "some.name.space",
  "types" : [ ],
  "messages" : { "go" : { "request" : [ ], "response" : "null" } }
}
""");

        Assert.That(AvroNameValidation.FindUnusableNames(protocol), Is.Empty);
    }

    [TestCase("""{ "type" : "record", "name" : "Widget", "namespace" : "some.name.space", "fields" : [ ] }""")]
    [TestCase("""{ "type" : "enum", "name" : "Kind", "namespace" : "some.name.space", "symbols" : [ "A" ] }""")]
    [TestCase("""{ "type" : "fixed", "name" : "Hash", "namespace" : "some.name.space", "size" : 16 }""")]
    [TestCase("""{ "type" : "record", "name" : "Widget", "fields" : [ ] }""")]
    public static void FindUnusableNames_GivenUsableSchemaNames_ReturnsNothing(string json)
    {
        var schema = (NamedSchema)Schema.Parse(json);

        Assert.That(AvroNameValidation.FindUnusableNames(schema), Is.Empty);
    }

    [TestCase("""{ "type" : "record", "name" : "Widget", "namespace" : "my-ns", "fields" : [ ] }""")]
    [TestCase("""{ "type" : "enum", "name" : "Kind", "namespace" : "my-ns", "symbols" : [ "A" ] }""")]
    [TestCase("""{ "type" : "fixed", "name" : "Hash", "namespace" : "my-ns", "size" : 16 }""")]
    public static void FindUnusableNames_GivenSchemaNamespaceCsharpCannotSpell_ReturnsTheNamespace(string json)
    {
        var schema = (NamedSchema)Schema.Parse(json);

        Assert.That(AvroNameValidation.FindUnusableNames(schema), Is.EqualTo(new[] { "my-ns" }));
    }

    [TestCase("some.1name.space")]
    [TestCase("some. .space")]
    [TestCase("some.name space")]
    public static void FindUnusableNames_GivenNamespaceSegmentCsharpCannotSpell_ReturnsTheWholeNamespace(string declaredNamespace)
    {
        var schema = (NamedSchema)Schema.Parse($$"""
{ "type" : "record", "name" : "Widget", "namespace" : "{{declaredNamespace}}", "fields" : [ ] }
""");

        Assert.That(AvroNameValidation.FindUnusableNames(schema), Is.EqualTo(new[] { declaredNamespace }));
    }
}
