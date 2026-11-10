using Avro;
using NUnit.Framework;
using SJP.Avro.Tools.Idl;

namespace SJP.Avro.Tools.Tests.Idl;

[TestFixture]
internal class IdlParseResultTests
{
    private const string ProtocolJson = """{ "protocol": "P", "namespace": "com.example", "types": [], "messages": {} }""";
    private const string SchemaJson = """{ "type": "record", "name": "R", "namespace": "com.example", "fields": [] }""";

    [Test]
    public void Protocol_GivenNullProtocol_ThrowsArgumentNullException()
    {
        Assert.That(() => IdlParseResult.Protocol(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void Schema_GivenNullSchema_ThrowsArgumentNullException()
    {
        Assert.That(() => IdlParseResult.Schema(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void Document_GivenProtocolResult_HoldsTheProtocol()
    {
        var protocol = Protocol.Parse(ProtocolJson);
        var result = IdlParseResult.Protocol(protocol);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Document is Protocol, Is.True);
            Assert.That(result.Document.Value, Is.SameAs(protocol));
        }
    }

    [Test]
    public void Document_GivenSchemaResult_HoldsTheSchema()
    {
        var schema = Schema.Parse(SchemaJson);
        var result = IdlParseResult.Schema(schema);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Document is Schema, Is.True);
            Assert.That(result.Document.Value, Is.SameAs(schema));
        }
    }
}
