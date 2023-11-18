using Superpower;
using Superpower.Model;
using Superpower.Parsers;

namespace SJP.Avro.Tools.Idl;

internal static class IdlTextParsers
{
    private static readonly string[] ExpectedSignOrLetter = ["sign", "infinity"];
    private static readonly string[] ExpectedInfinity = ["infinity"];

    /// <summary>
    /// Infinity optional +/- sign.
    /// </summary>
    public static TextParser<TextSpan> IdlInfinity { get; } = input =>
    {
        var next = input.ConsumeChar();

        if (!next.HasValue)
            return Result.Empty<TextSpan>(input, ExpectedSignOrLetter);

        if (next.Value == '-' || next.Value == '+')
            next = next.Remainder.ConsumeChar();

        if (!next.HasValue || !next.Value.IsLetter())
            return Result.Empty<TextSpan>(input, ExpectedInfinity);

        return Span.EqualToIgnoreCase("infinity")(next.Location);
    };

    public static TextParser<TextSpan> IdlNumber { get; } =
        Span.MatchedBy(
            Numerics.Integer
                .Or(Numerics.Decimal)
                .Or(Span.EqualTo("0x").IgnoreThen(Numerics.HexDigits))
                .Or(IdlInfinity));

    public static TextParser<string> QuotedIdentifier { get; } =
        Character.EqualTo('`')
            .IgnoreThen(Character.Except('`').AtLeastOnce())
            .Then(s => Character.EqualTo('`').Value(new string(s)));

    public static TextParser<string> IdlString { get; } = QuotedString.CStyle;

    public static TextParser<TextSpan> IdlIdentifier { get; } = Identifier.CStyle;

    public static TextParser<TextSpan> IdlPropertyName { get; } =
        Span.MatchedBy(
            Character.EqualTo('@')
                .IgnoreThen(
                    IdlIdentifier.ManyDelimitedBy(Character.EqualTo('-').Or(Character.EqualTo('.')))));

    /// <summary>
    /// Parses a Java-style multiline comment beginning with `/**` and ending with `*/`.
    /// </summary>
    public static TextParser<TextSpan> IdlDocComment
    {
        get
        {
            var beginDocComment = Span.EqualTo("/**");
            var endDocComment = Span.EqualTo("*/");

            return i =>
            {
                var begin = beginDocComment(i);
                if (!begin.HasValue)
                    return begin;

                var content = begin.Remainder;
                while (!content.IsAtEnd)
                {
                    var end = endDocComment(content);
                    if (end.HasValue)
                        return Result.Value(i.Until(end.Remainder), i, end.Remainder);

                    content = content.ConsumeChar().Remainder;
                }

                return endDocComment(content); // Will fail, because we're at the end-of-input.
            };
        }
    }

    public static TextParser<TextSpan> IdlComment { get; } = Comment.CStyle.Try().Or(Comment.CPlusPlusStyle);

    public static TextParser<TextSpan> Real { get; } = Numerics.Decimal;
}