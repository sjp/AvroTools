using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using SJP.Avro.Tools.Idl;
using SJP.Avro.Tools.Idl.Model;

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

    [Test]
    public void Compile_GivenNullFilePath_ThrowsArgNullException()
    {
        var testProtocol = new Protocol(
            null,
            new Identifier("fake_protocol"),
            [],
            [],
            [],
            [],
            [],
            [],
            new Dictionary<Identifier, MessageDeclaration>()
        );

        Assert.That(() => _compiler.Compile(null, testProtocol), Throws.ArgumentNullException);
    }

    [TestCase("")]
    [TestCase("        ")]
    public void Compile_GivenEmptyOrWhiteSpaceFilePath_ThrowsArgException(string filePath)
    {
        var testProtocol = new Protocol(
            null,
            new Identifier("fake_protocol"),
            [],
            [],
            [],
            [],
            [],
            [],
            new Dictionary<Identifier, MessageDeclaration>()
        );

        Assert.That(() => _compiler.Compile(filePath, testProtocol), Throws.ArgumentException);
    }

    [Test]
    public void Compile_GivenNullProtocol_ThrowsArgNullException()
    {
        Assert.That(() => _compiler.Compile("fake_path", null), Throws.ArgumentNullException);
    }

    [TestCaseSource(nameof(IdlInputOutputFilenames))]
    public static void Tokenize_GivenValidIdlInput_MatchesExpectedOutput(string idlSampleResourceName, string avroSampleResourceOutput)
    {
        var input = EmbeddedResource.GetByName(idlSampleResourceName);
        var output = EmbeddedResource.GetByName(avroSampleResourceOutput);

        var inputFileName = idlSampleResourceName.Split('.').TakeLast(2).Join(".");

        // TODO this is a broken path
        var basePath = @"C:\Users\sjp\source\repos\AvroTools\src\SJP.Avro.Tools.Tests\Idl\Data\Input";
        var resolvedResourceName = Path.Combine(basePath, inputFileName);
        var avpr = IdlToAvroTranslator.ParseIdl(input, resolvedResourceName, new DefaultFileProvider());
        var avprTxt = avpr.ToString();
        var avpr2 = avpr.ToString();
    }

    private static IEnumerable<object[]> IdlInputOutputFilenames()
    {
        var allNames = EmbeddedResource.GetEmbeddedResourceNames();
        var inputNames = EmbeddedResource.GetEmbeddedResourceNames()
            .Where(n => n.EndsWith(".avdl"))
            .Order()
            .ToList();

        var protocolOutputFileNames = inputNames
            .Select(n => n.Replace(".avdl", ".avpr"))
            .ToHashSet();
        var schemaOutputFileNames = inputNames
            .Select(n => n.Replace(".avdl", ".avsc"))
            .ToHashSet();

        var outputNames = EmbeddedResource.GetEmbeddedResourceNames()
            .Where(n => n.Contains(".Output.") && (protocolOutputFileNames.Contains(n.Replace(".Output.", ".Input.")) || schemaOutputFileNames.Contains(n.Replace(".Output.", ".Input."))))
            .Order()
            .ToList();

        return inputNames
            .Zip(outputNames, (a, b) => new object[] { a, b })
            .ToList();
    }
}