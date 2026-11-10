using System;
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

    // The members below are obsolete in favour of Document, but remain part of the public API,
    // so their behaviour stays covered.
#pragma warning disable CS0618 // Type or member is obsolete
    [Test]
    public void ObsoleteMembers_GivenProtocolResult_ReportAndMatchTheProtocol()
    {
        var result = IdlParseResult.Protocol(Protocol.Parse(ProtocolJson));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsProtocol, Is.True);
            Assert.That(result.IsSchema, Is.False);
            Assert.That(result.Match(p => p.Name, s => s.Name + "-schema"), Is.EqualTo("P"));
        }
    }

    [Test]
    public void ObsoleteMembers_GivenSchemaResult_ReportAndMatchTheSchema()
    {
        var result = IdlParseResult.Schema(Schema.Parse(SchemaJson));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsProtocol, Is.False);
            Assert.That(result.IsSchema, Is.True);
            Assert.That(result.Match(p => p.Name + "-protocol", s => s.Name), Is.EqualTo("R"));
        }
    }

    [Test]
    public void Match_GivenActionsForProtocolResult_InvokesOnlyTheProtocolAction()
    {
        var result = IdlParseResult.Protocol(Protocol.Parse(ProtocolJson));
        var protocolCalls = 0;
        var schemaCalls = 0;

        result.Match(_ => { protocolCalls++; }, _ => { schemaCalls++; });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(protocolCalls, Is.EqualTo(1));
            Assert.That(schemaCalls, Is.Zero);
        }
    }

    [Test]
    public void Match_GivenActionsForSchemaResult_InvokesOnlyTheSchemaAction()
    {
        var result = IdlParseResult.Schema(Schema.Parse(SchemaJson));
        var protocolCalls = 0;
        var schemaCalls = 0;

        result.Match(_ => { protocolCalls++; }, _ => { schemaCalls++; });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(protocolCalls, Is.Zero);
            Assert.That(schemaCalls, Is.EqualTo(1));
        }
    }

    [Test]
    public void Match_GivenNullCallbacks_ThrowsArgumentNullException()
    {
        var result = IdlParseResult.Schema(Schema.Parse(SchemaJson));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => result.Match(null!, (Action<Schema>)(_ => { })), Throws.ArgumentNullException);
            Assert.That(() => result.Match((Action<Protocol>)(_ => { }), null!), Throws.ArgumentNullException);
            Assert.That(() => result.Match(null!, (Func<Schema, int>)(_ => 0)), Throws.ArgumentNullException);
            Assert.That(() => result.Match((Func<Protocol, int>)(_ => 0), null!), Throws.ArgumentNullException);
        }
    }
#pragma warning restore CS0618 // Type or member is obsolete
}
