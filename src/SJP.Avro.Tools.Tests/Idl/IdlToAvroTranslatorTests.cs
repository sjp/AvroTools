using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using JsonDiffPatch;
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SJP.Avro.Tools.Idl;

namespace SJP.Avro.Tools.Tests.Idl;

[TestFixture]
internal static class IdlToAvroTranslatorTests
{
    private const string BaseInputNamespace = "SJP.Avro.Tools.Tests.Idl.Data.Input";
    private const string BaseOutputNamespace = "SJP.Avro.Tools.Tests.Idl.Data.Output";

    private static readonly IFileProvider InputFileProvider = new EmbeddedFileProvider(Assembly.GetExecutingAssembly(), BaseInputNamespace);
    private static readonly IFileProvider OutputFileProvider = new EmbeddedFileProvider(Assembly.GetExecutingAssembly(), BaseOutputNamespace);

    [TestCaseSource(nameof(IdlInputOutputFilenames))]
    public static async Task Tokenize_GivenValidIdlInput_MatchesExpectedOutput(string idlSampleResourceName, string avroSampleResourceOutput)
    {
        var inputFile = InputFileProvider.GetFileInfo(idlSampleResourceName);
        var outputFile = OutputFileProvider.GetFileInfo(avroSampleResourceOutput);

        await using var inputFileReadStream = inputFile.CreateReadStream();
        await using var outputFileReadStream = outputFile.CreateReadStream();

        using var inputReader = new StreamReader(inputFileReadStream);
        using var outputReader = new StreamReader(outputFileReadStream);

        var inputContents = await inputReader.ReadToEndAsync();
        var outputContents = await outputReader.ReadToEndAsync();

        var parseResult = IdlToAvroTranslator.ParseIdl(inputContents, idlSampleResourceName, InputFileProvider);
        var jsonText = parseResult.Match(p => p.ToString(), s => s.ToString());

        //var differ = new JsonDiffer();
        //var diffResult = differ.Diff(JObject.Parse(jsonText), JObject.Parse(outputContents), true);
        //Assert.That(diffResult.Operations, Is.Empty);

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