using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SJP.Avro.Tools.Idl;
using Superpower.Model;

namespace SJP.Avro.Tools.Tests.Idl;

[TestFixture]
internal static class IdlTokenParserTests
{
    [TestCaseSource(nameof(IdlSampleFilenames))]
    public static void ProtocolParse_GivenValidIdlInput_ReturnsProtocol(string idlSampleResourceName)
    {
        var input = EmbeddedResource.GetByName(idlSampleResourceName);

        var tokenizer = new IdlTokenizer();
        var tokenizeResult = tokenizer.TryTokenize(input);
        var tokens = tokenizeResult.Value.ToList();

        var commentFreeTokens = tokens.Where(t => t.Kind != IdlToken.Comment).ToArray();
        var tokenList = new TokenList<IdlToken>(commentFreeTokens);

        var result = IdlTokenParsers.Protocol(tokenList);

        Assert.That(result.HasValue, Is.True);
        Assert.That(result.Value, Is.Not.Null);
    }

    private static IEnumerable<string> IdlSampleFilenames()
    {
        return EmbeddedResource.GetEmbeddedResourceNames()
            .Where(n => n.EndsWith(".avdl"))
            .OrderBy(n => n)
            .ToList();
    }
}