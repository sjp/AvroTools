using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SJP.Avro.Tools.Idl;
using SJP.Avro.Tools.Idl.Model;
using Superpower.Model;

namespace SJP.Avro.Tools.Tests.Idl;

[TestFixture]
internal class IdlCompilerTests
{
    private IdlCompiler _compiler;

    [SetUp]
    public void Setup()
    {
        var fileProvider = EmbeddedResourceFileProvider.Instance;
        _compiler = new IdlCompiler(fileProvider);
    }

    [Test]
    public static void Ctor_GivenNullFileProvider_ThrowsArgNullException()
    {
        Assert.That(() => new IdlCompiler(null), Throws.ArgumentNullException);
    }

    [TestCase((string)null)]
    [TestCase("")]
    [TestCase("        ")]
    public void Compile_GivenNullFilePath_ThrowsArgNullException(string filePath)
    {
        var testProtocol = new Protocol(
            null,
            new Identifier("fake_protocol"),
            Array.Empty<Property>(),
            Array.Empty<RecordDeclaration>(),
            Array.Empty<FixedDeclaration>(),
            Array.Empty<EnumDeclaration>(),
            Array.Empty<ErrorDeclaration>(),
            Array.Empty<ImportDeclaration>(),
            new Dictionary<Identifier, MessageDeclaration>()
        );

        Assert.That(() => _compiler.Compile(filePath, testProtocol), Throws.ArgumentNullException);
    }

    [Test]
    public void Compile_GivenNullProtocol_ThrowsArgNullException()
    {
        Assert.That(() => _compiler.Compile("fake_path", null), Throws.ArgumentNullException);
    }

    [TestCaseSource(nameof(IdlSampleFilenames))]
    public static void Tokenize_GivenValidIdlInput_CompilesJson(string idlSampleResourceName)
    {
        var input = EmbeddedResource.GetByName(idlSampleResourceName);

        var tokenizer = new IdlTokenizer();
        var tokenizeResult = tokenizer.TryTokenize(input);
        var tokens = tokenizeResult.Value.ToList();

        var commentFreeTokens = tokens.Where(t => t.Kind != IdlToken.Comment).ToArray();
        var tokenList = new TokenList<IdlToken>(commentFreeTokens);

        var result = IdlTokenParsers.Protocol(tokenList);
        var protocol = result.Value;

        var compiler = new IdlCompiler(EmbeddedResourceFileProvider.Instance);
        var json = compiler.Compile(idlSampleResourceName, protocol);

        Assert.That(json, Is.Not.Null);
        Assert.That(json, Is.Not.Empty);
    }

    [TestCaseSource(nameof(IdlInputOutputFilenames))]
    public static void Tokenize_GivenValidIdlInput_MatchesExpectedOutput(string idlSampleResourceName, string avroSampleResourceOutput)
    {
        var input = EmbeddedResource.GetByName(idlSampleResourceName);
        var output = EmbeddedResource.GetByName(avroSampleResourceOutput);

        var tokenizer = new IdlTokenizer();
        var tokenizeResult = tokenizer.TryTokenize(input);
        var tokens = tokenizeResult.Value.ToList();

        var commentFreeTokens = tokens.Where(t => t.Kind != IdlToken.Comment).ToArray();
        var tokenList = new TokenList<IdlToken>(commentFreeTokens);

        var result = IdlTokenParsers.Protocol(tokenList);
        var protocol = result.Value;

        var compiler = new IdlCompiler(EmbeddedResourceFileProvider.Instance);
        var json = compiler.Compile(idlSampleResourceName, protocol);

        var parsedExpectedOutput = JToken.Parse(output);
        var parsedOutput = JToken.Parse(json);

        var differ = new JsonDiffPatch.JsonDiffer();
        var patched = differ.Diff(parsedOutput, parsedExpectedOutput, false);

        Assert.That(patched.Operations, Is.Empty);
    }

    private static IEnumerable<string> IdlSampleFilenames()
    {
        return EmbeddedResource.GetEmbeddedResourceNames()
            .Where(n => n.EndsWith(".avdl"))
            .OrderBy(n => n)
            .ToList();
    }

    private static IEnumerable<object[]> IdlInputOutputFilenames()
    {
        var inputNames = EmbeddedResource.GetEmbeddedResourceNames()
            .Where(n => n.EndsWith(".avdl"))
            .OrderBy(n => n)
            .ToList();

        var outputNames = EmbeddedResource.GetEmbeddedResourceNames()
            .Where(n => n.Contains(".Output.") && n.EndsWith(".avpr"))
            .OrderBy(n => n)
            .ToList();

        return inputNames
            .Zip(outputNames, (a, b) => new object[] { a, b })
            .ToList();
    }
}