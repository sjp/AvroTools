using System.Collections.Generic;
using System.Linq;
using Superpower;
using Superpower.Model;

namespace SJP.Avro.Tools.Idl;

internal static class ParsingExtensions
{
    public static TokenListParser<TKind, Token<TKind>> NotEqualTo<TKind>(this IEnumerable<TKind> kinds)
    {
        var expectations = new[]
        {
                kinds.Select(k => k?.ToString() ?? string.Empty)
                    .Where(static k => k.Length > 0)
                    .Join(", ")
            };

        return input =>
        {
            var next = input.ConsumeToken();
            if (!next.HasValue || kinds.Any(k => next.Value.Kind != null && next.Value.Kind.Equals(k)))
                return TokenListParserResult.Empty<TKind, Token<TKind>>(input, expectations);

            return next;
        };
    }
}
