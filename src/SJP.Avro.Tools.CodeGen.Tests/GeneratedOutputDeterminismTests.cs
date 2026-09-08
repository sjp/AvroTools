using System;
using System.Globalization;
using System.Threading;
using Avro;
using NUnit.Framework;

namespace SJP.Avro.Tools.CodeGen.Tests;

/// <summary>
/// Generated code is expected to be byte-for-byte identical wherever it is produced, so that a
/// generated file kept in source control does not differ between contributors' machines.
/// </summary>
[TestFixture]
internal static class GeneratedOutputDeterminismTests
{
    private const string TestNamespace = "Test.Avro.Namespace";

    // Turkish casing maps 'i' onto 'İ' rather than 'I', so a name beginning with 'i' shows up any
    // casing that is left to the current culture.
    private const string TurkishCulture = "tr-TR";

    private const string RecordJson = """
{
    "type": "record",
    "name": "invoice",
    "doc": "A record whose name begins with a dotless i.",
    "namespace": "avro.examples.billing",
    "fields": [
        { "name": "id", "type": "int" },
        { "name": "issued", "type": "string", "doc": "When it was issued." }
    ]
}
""";

    private const string EnumJson = """
{
    "type": "enum",
    "name": "iso",
    "doc": "An enumeration whose name begins with a dotless i.",
    "namespace": "avro.examples.billing",
    "symbols": [ "A", "B" ]
}
""";

    private const string FixedJson = """
{
    "type": "fixed",
    "name": "id",
    "namespace": "avro.examples.billing",
    "size": 16
}
""";

    private const string ProtocolJson = """
{
    "protocol": "invoicing",
    "namespace": "avro.examples.billing",
    "doc": "A protocol whose name begins with a dotless i.",
    "types": [
        {
            "type": "record",
            "name": "invoice",
            "fields": [ { "name": "id", "type": "int" } ]
        }
    ],
    "messages": {
        "send": {
            "request": [ { "name": "value", "type": "invoice" } ],
            "response": "null"
        }
    }
}
""";

    private static string GenerateRecord() =>
        new AvroRecordGenerator().Generate((RecordSchema)Schema.Parse(RecordJson), TestNamespace);

    private static string GenerateEnum() =>
        new AvroEnumGenerator().Generate((EnumSchema)Schema.Parse(EnumJson), TestNamespace);

    private static string GenerateFixed() =>
        new AvroFixedGenerator().Generate((FixedSchema)Schema.Parse(FixedJson), TestNamespace);

    private static string GenerateProtocol() =>
        new AvroProtocolGenerator().Generate(Protocol.Parse(ProtocolJson), TestNamespace)!;

    private static readonly Func<string>[] Generators =
        [GenerateRecord, GenerateEnum, GenerateFixed, GenerateProtocol];

    [TestCaseSource(nameof(Generators))]
    public static void Generate_WhenCurrentCultureDiffers_ProducesIdenticalOutput(Func<string> generate)
    {
        var expected = InCulture(CultureInfo.InvariantCulture, generate);
        var turkish = InCulture(GetTurkishCulture(), generate);

        Assert.That(turkish, Is.EqualTo(expected));
    }

    [TestCaseSource(nameof(Generators))]
    public static void Generate_WhateverTheHostLineEnding_UsesLineFeedsOnly(Func<string> generate)
    {
        Assert.That(generate(), Does.Not.Contain("\r"));
    }

    private static CultureInfo GetTurkishCulture()
    {
        try
        {
            return CultureInfo.GetCultureInfo(TurkishCulture);
        }
        catch (CultureNotFoundException)
        {
            Assert.Ignore($"The '{TurkishCulture}' culture is unavailable on this host.");
            throw;
        }
    }

    private static string InCulture(CultureInfo culture, Func<string> generate)
    {
        var previousCulture = Thread.CurrentThread.CurrentCulture;
        var previousUiCulture = Thread.CurrentThread.CurrentUICulture;

        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        try
        {
            return generate();
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previousCulture;
            Thread.CurrentThread.CurrentUICulture = previousUiCulture;
        }
    }
}
