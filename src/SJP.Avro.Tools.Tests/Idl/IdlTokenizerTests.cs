using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SJP.Avro.Tools.Idl;

namespace SJP.Avro.Tools.Tests.Idl;

[TestFixture]
internal static class IdlTokenizerTests
{
    [TestCaseSource(nameof(IdlSampleFilenames))]
    public static void Tokenize_GivenValidIdlInput_ReturnsValidTokens(string idlSampleResourceName)
    {
        var input = EmbeddedResource.GetByName(idlSampleResourceName);

        var tokenizer = new IdlTokenizer();
        var tokenizeResult = tokenizer.TryTokenize(input);
        var tokens = tokenizeResult.Value.ToList();

        Assert.That(tokenizeResult.HasValue, Is.True);
        Assert.That(tokens, Is.Not.Empty);
    }

    private static IEnumerable<string> IdlSampleFilenames()
    {
        return EmbeddedResource.GetEmbeddedResourceNames()
            .Where(n => n.EndsWith(".avdl"))
            .OrderBy(n => n)
            .ToList();
    }
}