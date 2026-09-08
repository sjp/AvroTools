using System;
using Avro;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SJP.Avro.Tools.CodeGen.Tests;

/// <summary>
/// Generated code is nullable-annotated: an optional field is typed <c>string?</c>, a required
/// reference-typed one is initialised with <c>default!</c>, and <c>Get</c> hands back whichever of
/// them the field position names. Those annotations only mean anything inside a nullable context,
/// and outside one they are warnings in their own right, so every generated file opens by enabling
/// the context for itself. These tests hold the output to that on both sides: a project that has
/// not opted in must not be warned about the annotations, and one that has must not be warned about
/// what they say.
/// </summary>
[TestFixture]
internal static class GeneratedCodeNullabilityTests
{
    private const string TestNamespace = "Test.Avro.Nullability";

    private const string NullableDirective = "#nullable enable";

    /// <summary>
    /// A record that reaches every shape the generator annotates: an optional primitive, an
    /// optional named type, optional collections, a required reference type, and a recursive
    /// reference back to the record itself.
    /// </summary>
    private const string RecordJson = $$"""
{
    "type": "record",
    "name": "Optionals",
    "namespace": "{{TestNamespace}}",
    "fields": [
        { "name": "required", "type": "string" },
        { "name": "optionalText", "type": [ "null", "string" ] },
        { "name": "optionalNumber", "type": [ "null", "int" ] },
        { "name": "optionalBytes", "type": [ "null", "bytes" ] },
        {
            "name": "optionalKind",
            "type": [ "null", { "type": "enum", "name": "Kind", "symbols": [ "A", "B" ] } ]
        },
        {
            "name": "optionalHash",
            "type": [ "null", { "type": "fixed", "name": "Hash", "size": 4 } ]
        },
        {
            "name": "optionalList",
            "type": [ "null", { "type": "array", "items": "string" } ]
        },
        {
            "name": "optionalLookup",
            "type": [ "null", { "type": "map", "values": "string" } ]
        },
        {
            "name": "optionalDecimal",
            "type": [ "null", { "type": "bytes", "logicalType": "decimal", "precision": 9, "scale": 2 } ]
        },
        { "name": "optionalEither", "type": [ "null", "int", "string" ] },
        { "name": "optionalSelf", "type": [ "null", "Optionals" ] }
    ]
}
""";

    private const string ProtocolJson = $$"""
{
    "protocol": "Optionality",
    "namespace": "{{TestNamespace}}",
    "types": [
        {
            "type": "record",
            "name": "Payload",
            "fields": [ { "name": "text", "type": [ "null", "string" ] } ]
        }
    ],
    "messages": {
        "send": {
            "request": [ { "name": "payload", "type": "Payload" } ],
            "response": "null"
        }
    }
}
""";

    private const string EnumJson =
        $$"""{"type":"enum","name":"Kind","namespace":"{{TestNamespace}}","symbols":["A","B"]}""";

    private const string FixedJson =
        $$"""{"type":"fixed","name":"Hash","namespace":"{{TestNamespace}}","size":4}""";

    private static string GenerateRecord() =>
        new AvroRecordGenerator().Generate((RecordSchema)Schema.Parse(RecordJson), TestNamespace);

    private static string GenerateEnum() =>
        new AvroEnumGenerator().Generate((EnumSchema)Schema.Parse(EnumJson), TestNamespace);

    private static string GenerateFixed() =>
        new AvroFixedGenerator().Generate((FixedSchema)Schema.Parse(FixedJson), TestNamespace);

    /// <summary>
    /// The record and the named types it refers to, which the generator emits as files of their own.
    /// </summary>
    private static string[] GenerateRecordFiles() => [GenerateRecord(), GenerateEnum(), GenerateFixed()];

    /// <summary>
    /// The same fields declared on an error rather than a record, which is generated as a class
    /// deriving from <c>SpecificException</c> and so overrides
    /// <c>Get</c> instead of implementing it.
    /// </summary>
    private static string[] GenerateErrorFiles()
    {
        var errorJson = RecordJson.Replace("\"type\": \"record\"", "\"type\": \"error\"", StringComparison.Ordinal);
        var errorSchema = (RecordSchema)Schema.Parse(errorJson);

        return [new AvroRecordGenerator().Generate(errorSchema, TestNamespace), GenerateEnum(), GenerateFixed()];
    }

    private static string[] GenerateEnumFiles() => [GenerateEnum()];

    private static string[] GenerateFixedFiles() => [GenerateFixed()];

    private static string[] GenerateProtocolFiles()
    {
        var protocol = Protocol.Parse(ProtocolJson);

        return
        [
            new AvroProtocolGenerator().Generate(protocol, TestNamespace)!,
            new AvroRecordGenerator().Generate((RecordSchema)protocol.Types[0], TestNamespace)
        ];
    }

    private static readonly Func<string[]>[] Generators =
        [GenerateRecordFiles, GenerateErrorFiles, GenerateEnumFiles, GenerateFixedFiles, GenerateProtocolFiles];

    [TestCaseSource(nameof(Generators))]
    public static void Generate_ForEveryGeneratedFile_EnablesTheNullableContextBeforeAnythingElse(Func<string[]> generate)
    {
        Assert.That(generate(), Is.All.StartsWith(NullableDirective));
    }

    [TestCaseSource(nameof(Generators))]
    public static void Generate_WhenTheConsumingProjectHasNotEnabledNullable_CompilesWithoutWarnings(Func<string[]> generate)
    {
        Assert.That(
            () => GeneratedSourceCompiler.Compile(NullableContextOptions.Disable, generate()),
            Throws.Nothing);
    }

    [TestCaseSource(nameof(Generators))]
    public static void Generate_WhenTheConsumingProjectHasEnabledNullable_CompilesWithoutWarnings(Func<string[]> generate)
    {
        Assert.That(
            () => GeneratedSourceCompiler.Compile(NullableContextOptions.Enable, generate()),
            Throws.Nothing);
    }

    [Test]
    public static void Generate_GivenRecordWithOptionalFields_AnnotatesThemForACallerThatHasEnabledNullable()
    {
        // The consumer is written as a nullable-enabled project would write it: an optional field
        // is read into a nullable local and a required one into a non-nullable local. Compiled with
        // warnings as errors, it fails if the generated annotations say anything else.
        const string consumer = $$"""
#nullable enable

namespace {{TestNamespace}}.Consumer
{
    internal static class Reader
    {
        public static void Read(global::{{TestNamespace}}.Optionals record)
        {
            string required = record.required;
            string? optionalText = record.optionalText;
            int? optionalNumber = record.optionalNumber;
            global::{{TestNamespace}}.Optionals? optionalSelf = record.optionalSelf;
            object? read = record.Get(0);

            global::System.GC.KeepAlive((required, optionalText, optionalNumber, optionalSelf, read));
        }
    }
}
""";

        Assert.That(
            () => GeneratedSourceCompiler.Compile(NullableContextOptions.Enable, [.. GenerateRecordFiles(), consumer]),
            Throws.Nothing);
    }
}
