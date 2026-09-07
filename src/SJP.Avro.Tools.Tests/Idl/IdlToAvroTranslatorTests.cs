using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
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

    private const string InputResourcePrefix = "Idl.Data.Input.";
    private const string OutputResourcePrefix = "Idl.Data.Output.";

    private static readonly string[] OutputExtensions = [".avpr", ".avsc"];

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

        Assert.That(outputFile.Exists, Is.True, $"No expected output was found for the input '{idlSampleResourceName}', looked for '{avroSampleResourceOutput}'.");

        await using var outputFileReadStream = outputFile.CreateReadStream();
        using var outputReader = new StreamReader(outputFileReadStream);
        var outputContents = await outputReader.ReadToEndAsync(TestContext.CurrentContext.CancellationToken);

        await using var inputFileReadStream = inputFile.CreateReadStream();
        var parseResult = await _translator.Translate(inputFileReadStream, TestContext.CurrentContext.CancellationToken);
        var jsonText = parseResult.Match(p => p.ToString(), s => s.ToString());

        var patcher = new JsonDiffPatch();
        var diffResult = patcher.Diff(JObject.Parse(jsonText), JObject.Parse(outputContents));

        Assert.That(diffResult, Is.Null);
    }

    [Test]
    public void Translate_GivenTextThatCannotBeTokenised_ThrowsWithPosition()
    {
        const string idl = "protocol TestProtocol { record TestRecord { string a; # } }";

        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() => _translator.Translate(idl, TestContext.CurrentContext.CancellationToken));

        Assert.That(thrown.Message, Does.Contain("Syntax error at line 1:54"));
    }

    [Test]
    public void Translate_GivenStreamThatCannotBeTokenised_ThrowsWithPosition()
    {
        const string idl = "protocol TestProtocol { record TestRecord { string a; # } }";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(idl));

        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() => _translator.Translate(stream, TestContext.CurrentContext.CancellationToken));

        Assert.That(thrown.Message, Does.Contain("Syntax error at line 1:54"));
    }

    private static IEnumerable<object[]> IdlInputOutputFilenames()
    {
        var resourceNames = EmbeddedResource.GetEmbeddedResourceNames().ToHashSet(StringComparer.Ordinal);

        return resourceNames
            .Where(n => n.StartsWith(InputResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".avdl", StringComparison.Ordinal))
            .Select(n => n[InputResourcePrefix.Length..])
            .Order(StringComparer.Ordinal)
            .Select(inputName =>
            {
                var candidateOutputNames = Array.ConvertAll(OutputExtensions, ext => Path.ChangeExtension(inputName, ext));
                var outputName = Array.Find(candidateOutputNames, name => resourceNames.Contains(OutputResourcePrefix + name))
                    ?? candidateOutputNames[0];

                return new object[] { inputName, outputName };
            })
            .ToList();
    }
}
