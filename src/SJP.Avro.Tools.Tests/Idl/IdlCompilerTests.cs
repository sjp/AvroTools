using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using NUnit.Framework;
using SJP.Avro.Tools.Idl;
using SJP.Avro.Tools.Idl.Model;

namespace SJP.Avro.Tools.Tests.Idl;

[TestFixture]
internal class IdlCompilerTests
{
    private const string BaseInputNamespace = "SJP.Avro.Tools.Tests.Idl.Data.Input";
    private const string BaseOutputNamespace = "SJP.Avro.Tools.Tests.Idl.Data.Output";

    private static readonly IFileProvider InputFileProvider = new EmbeddedFileProvider(Assembly.GetExecutingAssembly(), BaseInputNamespace);
    private static readonly IFileProvider OutputFileProvider = new EmbeddedFileProvider(Assembly.GetExecutingAssembly(), BaseOutputNamespace);

    private IdlCompiler _compiler;

    [SetUp]
    public void Setup()
    {
        var fileProvider = new EmbeddedFileProvider(Assembly.GetExecutingAssembly());
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
    public static async Task Tokenize_GivenValidIdlInput_MatchesExpectedOutput(string idlSampleResourceName, string avroSampleResourceOutput)
    {
        var inputFile = InputFileProvider.GetFileInfo(idlSampleResourceName);
        var outputFile = OutputFileProvider.GetFileInfo(avroSampleResourceOutput);

        using var inputReader = new StreamReader(inputFile.CreateReadStream());
        using var outputReader = new StreamReader(outputFile.CreateReadStream());

        var inputContents = await inputReader.ReadToEndAsync();
        var outputContents = await outputReader.ReadToEndAsync();

        var avpr = IdlToAvroTranslator.ParseIdl(inputContents, idlSampleResourceName, InputFileProvider);
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
            .Zip(outputNames, (a, b) => new object[]
            {
                a.Replace("Idl.Data.Input.", string.Empty),
                b.Replace("Idl.Data.Output.", string.Empty)
            })
            .ToList();
    }
}