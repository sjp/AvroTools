using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using JsonDiffPatchDotNet;
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SJP.Avro.Tools.Idl;

namespace SJP.Avro.Tools.Tests.Idl;

[TestFixture]
internal class IdlToAvroTranslatorTests
{
    private const string BaseInputNamespace = "SJP.Avro.Tools.Tests.Idl.Data.Input";
    private const string BaseOutputNamespace = "SJP.Avro.Tools.Tests.Idl.Data.Output";

    private static readonly IFileProvider InputFileProvider = new EmbeddedFileProvider(Assembly.GetExecutingAssembly(), BaseInputNamespace);
    private static readonly IFileProvider OutputFileProvider = new EmbeddedFileProvider(Assembly.GetExecutingAssembly(), BaseOutputNamespace);

    private IdlToAvroTranslator _translator;

    [SetUp]
    public void Setup()
    {
        _translator = new IdlToAvroTranslator(new FileProviderIdlFileReader(InputFileProvider));
    }

    [TestCaseSource(nameof(IdlInputOutputFilenames))]
    public async Task ParseIdl_GivenValidIdlInput_MatchesExpectedOutput(string idlSampleResourceName, string avroSampleResourceOutput)
    {
        var inputFile = InputFileProvider.GetFileInfo(idlSampleResourceName);
        var outputFile = OutputFileProvider.GetFileInfo(avroSampleResourceOutput);

        await using var outputFileReadStream = outputFile.CreateReadStream();
        using var outputReader = new StreamReader(outputFileReadStream);
        var outputContents = await outputReader.ReadToEndAsync();

        await using var inputFileReadStream = inputFile.CreateReadStream();
        var parseResult = await _translator.Translate(inputFileReadStream);
        var jsonText = parseResult.Match(p => p.ToString(), s => s.ToString());

        var patcher = new JsonDiffPatch();
        var diffResult = patcher.Diff(JObject.Parse(jsonText), JObject.Parse(outputContents));

        Assert.That(diffResult, Is.Null);
    }

    [Test]
    public void Translate_GivenTextThatCannotBeTokenised_ThrowsWithPosition()
    {
        const string idl = "protocol TestProtocol { record TestRecord { string a; # } }";

        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() => _translator.Translate(idl));

        Assert.That(thrown.Message, Does.Contain("Syntax error at line 1:54"));
    }

    [Test]
    public void Translate_GivenStreamThatCannotBeTokenised_ThrowsWithPosition()
    {
        const string idl = "protocol TestProtocol { record TestRecord { string a; # } }";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(idl));

        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() => _translator.Translate(stream));

        Assert.That(thrown.Message, Does.Contain("Syntax error at line 1:54"));
    }

    private static IEnumerable<object[]> IdlInputOutputFilenames()
    {
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

[TestFixture]
internal class IdlImportResolutionTests
{
    private const string DeepIdl = "@namespace(\"nested\") protocol Deep { record DeepRecord { string y; } }";
    private const string InnerIdl = "@namespace(\"nested\") protocol Inner { import idl \"deep/deep.avdl\"; record InnerRecord { nested.DeepRecord d; } }";

    private TemporaryDirectory _tempDir;
    private IdlToAvroTranslator _translator;

    [SetUp]
    public void Setup()
    {
        _tempDir = new TemporaryDirectory();
        _translator = new IdlToAvroTranslator(new PhysicalIdlFileReader());
    }

    [TearDown]
    public void TearDown() => _tempDir.Dispose();

    [Test]
    public async Task Translate_GivenImportInAnotherDirectory_ResolvesRelativeToImportingFile()
    {
        _tempDir.WriteFile(Path.Combine("sub", "inner.avdl"), "@namespace(\"nested\") protocol Inner { record InnerRecord { string x; } }");
        var main = _tempDir.WriteFile(Path.Combine("sub", "main.avdl"), "protocol Main { import idl \"inner.avdl\"; record Outer { nested.InnerRecord i; } }");

        var result = await Translate(main);

        Assert.That(TypeNames(result), Is.EqualTo(new[] { "nested.InnerRecord", "Outer" }));
    }

    [Test]
    public async Task Translate_GivenNestedImportTwoLevelsDeep_ResolvesEachAgainstItsOwnDirectory()
    {
        _tempDir.WriteFile(Path.Combine("sub", "deep", "deep.avdl"), DeepIdl);
        _tempDir.WriteFile(Path.Combine("sub", "inner.avdl"), InnerIdl);
        var main = _tempDir.WriteFile(Path.Combine("sub", "main.avdl"), "protocol Main { import idl \"inner.avdl\"; record Outer { nested.InnerRecord i; } }");

        var result = await Translate(main);

        Assert.That(TypeNames(result), Is.EqualTo(new[] { "nested.DeepRecord", "nested.InnerRecord", "Outer" }));
    }

    [Test]
    public async Task Translate_GivenSameFileImportedByTwoSpellings_ImportsItOnce()
    {
        _tempDir.WriteFile(Path.Combine("sub", "inner.avdl"), "@namespace(\"nested\") protocol Inner { record InnerRecord { string x; } }");
        var main = _tempDir.WriteFile(
            Path.Combine("sub", "main.avdl"),
            "protocol Main { import idl \"inner.avdl\"; import idl \"./inner.avdl\"; record Outer { nested.InnerRecord i; } }");

        var result = await Translate(main);

        Assert.That(TypeNames(result), Is.EqualTo(new[] { "nested.InnerRecord", "Outer" }));
    }

    [Test]
    public async Task Translate_GivenAbsoluteImportPath_ResolvesIt()
    {
        var inner = _tempDir.WriteFile(Path.Combine("sub", "inner.avdl"), "@namespace(\"nested\") protocol Inner { record InnerRecord { string x; } }");
        var main = _tempDir.WriteFile(
            "main.avdl",
            $"protocol Main {{ import idl \"{inner.Replace("\\", "\\\\")}\"; record Outer {{ nested.InnerRecord i; }} }}");

        var result = await Translate(main);

        Assert.That(TypeNames(result), Is.EqualTo(new[] { "nested.InnerRecord", "Outer" }));
    }

    [Test]
    public async Task Translate_GivenContentReadFromElsewhereWithBaseDirectory_ResolvesAgainstThatDirectory()
    {
        _tempDir.WriteFile(Path.Combine("sub", "inner.avdl"), "@namespace(\"nested\") protocol Inner { record InnerRecord { string x; } }");
        const string idl = "protocol Main { import idl \"inner.avdl\"; record Outer { nested.InnerRecord i; } }";

        var result = await _translator.Translate(idl, Path.Combine(_tempDir.DirectoryPath, "sub"), default);

        Assert.That(TypeNames(result), Is.EqualTo(new[] { "nested.InnerRecord", "Outer" }));
    }

    [Test]
    public void Translate_GivenImportMissingFromTheImportingFilesDirectory_Throws()
    {
        _tempDir.WriteFile("inner.avdl", "@namespace(\"nested\") protocol Inner { record InnerRecord { string x; } }");
        var main = _tempDir.WriteFile(Path.Combine("sub", "main.avdl"), "protocol Main { import idl \"inner.avdl\"; record Outer { string x; } }");

        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() => Translate(main));

        Assert.That(thrown.Message, Does.Contain("Failed to import IDL"));
    }

    private Task<IdlParseResult> Translate(string filePath)
    {
        var content = File.ReadAllText(filePath);
        return _translator.Translate(content, Path.GetDirectoryName(filePath), default);
    }

    private static IEnumerable<string> TypeNames(IdlParseResult result) =>
        result.Match(p => p.Types.Select(t => t.Fullname).ToList(), s => [s.Fullname]);
}
