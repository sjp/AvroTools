using System;
using System.Collections.Generic;
using Superpower;
using Superpower.Model;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// A tokenizer for Avro IDL documents.
/// </summary>
/// <seealso cref="Tokenizer{TKind}" />
public class IdlTokenizer : Tokenizer<IdlToken>, IIdlTokenizer
{
    static IdlTokenizer()
    {
        SimpleOps['<'] = IdlToken.LessThan;
        SimpleOps['>'] = IdlToken.GreaterThan;
        SimpleOps['='] = IdlToken.Equals;
        SimpleOps[','] = IdlToken.Comma;
        SimpleOps['.'] = IdlToken.Dot;
        SimpleOps['('] = IdlToken.LParen;
        SimpleOps[')'] = IdlToken.RParen;
        SimpleOps['{'] = IdlToken.LBrace;
        SimpleOps['}'] = IdlToken.RBrace;
        SimpleOps['['] = IdlToken.LBracket;
        SimpleOps[']'] = IdlToken.RBracket;
        SimpleOps['`'] = IdlToken.Backtick;
        SimpleOps[';'] = IdlToken.Semicolon;
        SimpleOps[':'] = IdlToken.Colon;
        SimpleOps['@'] = IdlToken.At;
        SimpleOps['?'] = IdlToken.Optional;
    }

    /// <summary>
    /// Tokenizes Avro IDL text.
    /// </summary>
    /// <param name="span">The input span to tokenize.</param>
    /// <returns>A list of parsed tokens.</returns>
    protected override IEnumerable<Result<IdlToken>> Tokenize(TextSpan span)
    {
        var next = SkipWhiteSpace(span);
        if (!next.HasValue)
            yield break;

        do
        {
            if (next.Value == '/')
            {
                var docComment = IdlTextParsers.IdlDocComment(next.Location);
                var comment = IdlTextParsers.IdlComment(next.Location);
                if (docComment.HasValue)
                {
                    yield return Result.Value(IdlToken.DocComment, docComment.Location, docComment.Remainder);
                    next = docComment.Remainder.ConsumeChar();
                }
                else if (comment.HasValue)
                {
                    yield return Result.Value(IdlToken.Comment, comment.Location, comment.Remainder);
                    next = comment.Remainder.ConsumeChar();
                }
            }
            else if (next.Value == '"')
            {
                var str = IdlTextParsers.IdlString(next.Location);
                if (str.HasValue)
                {
                    yield return Result.Value(IdlToken.StringLiteral, str.Location, str.Remainder);
                    next = str.Remainder.ConsumeChar();
                }
            }
            else if (next.Value == '`')
            {
                var ident = IdlTextParsers.QuotedIdentifier(next.Location);
                if (ident.HasValue)
                {
                    yield return Result.Value(IdlToken.Identifier, ident.Location, ident.Remainder);
                    next = ident.Remainder.ConsumeChar();
                }
            }
            else if (next.Value.IsDigit() || next.Value == '-')
            {
                var real = IdlTextParsers.IdlNumber(next.Location);
                if (!real.HasValue)
                    yield return Result.CastEmpty<TextSpan, IdlToken>(real);
                else
                    yield return Result.Value(IdlToken.Number, real.Location, real.Remainder);

                next = real.Remainder.ConsumeChar();
            }
            else if (next.Value == '@')
            {
                var propertyName = IdlTextParsers.IdlPropertyName(next.Location);
                if (propertyName.HasValue)
                {
                    yield return Result.Value(IdlToken.PropertyName, propertyName.Location, propertyName.Remainder);
                    next = propertyName.Remainder.ConsumeChar();
                }
            }
            else if (next.Value.IsLetter())
            {
                var beginIdentifier = next.Location;
                do
                {
                    next = next.Remainder.ConsumeChar();
                }
                while (next.HasValue && (next.Value.IsLetterOrDigit() || next.Value == '_' || next.Value == '.'));

                if (TryGetKeyword(beginIdentifier.Until(next.Location), out var keyword))
                    yield return Result.Value(keyword, beginIdentifier, next.Location);
                else
                    yield return Result.Value(IdlToken.Identifier, beginIdentifier, next.Location);
            }
            else
            {
                if (next.Value < SimpleOps.Length && SimpleOps[next.Value] != IdlToken.None)
                {
                    yield return Result.Value(SimpleOps[next.Value], next.Location, next.Remainder);
                    next = next.Remainder.ConsumeChar();
                }
                else
                {
                    yield return Result.Empty<IdlToken>(next.Location);
                    next = next.Remainder.ConsumeChar();
                }
            }

            next = SkipWhiteSpace(next.Location);
        } while (next.HasValue);
    }

    private static bool TryGetKeyword(TextSpan span, out IdlToken keyword)
    {
        foreach (var kw in IdlKeywords)
        {
            if (span.EqualsValue(kw.Text))
            {
                keyword = kw.Token;
                return true;
            }
        }

        keyword = IdlToken.None;
        return false;
    }

    private static readonly IdlToken[] SimpleOps = new IdlToken[128];

    private static readonly IdlKeyword[] IdlKeywords =
    [
        new IdlKeyword("array", IdlToken.Array),
        new IdlKeyword("boolean", IdlToken.Boolean),
        new IdlKeyword("double", IdlToken.Double),
        new IdlKeyword("enum", IdlToken.Enum),
        new IdlKeyword("false", IdlToken.False),
        new IdlKeyword("fixed", IdlToken.Fixed),
        new IdlKeyword("float", IdlToken.Float),
        new IdlKeyword("idl", IdlToken.Idl),
        new IdlKeyword("import", IdlToken.Import),
        new IdlKeyword("int", IdlToken.Int),
        new IdlKeyword("long", IdlToken.Long),
        new IdlKeyword("map", IdlToken.Map),
        new IdlKeyword("oneway", IdlToken.Oneway),
        new IdlKeyword("bytes", IdlToken.Bytes),
        new IdlKeyword("schema", IdlToken.Schema),
        new IdlKeyword("string", IdlToken.String),
        new IdlKeyword("null", IdlToken.Null),
        new IdlKeyword("protocol", IdlToken.Protocol),
        new IdlKeyword("error", IdlToken.Error),
        new IdlKeyword("record", IdlToken.Record),
        new IdlKeyword("throws", IdlToken.Throws),
        new IdlKeyword("true", IdlToken.True),
        new IdlKeyword("union", IdlToken.Union),
        new IdlKeyword("void", IdlToken.Void),
        new IdlKeyword("date", IdlToken.Date),
        new IdlKeyword("time_ms", IdlToken.TimeMs),
        new IdlKeyword("time_micros", IdlToken.TimeMicros),
        new IdlKeyword("timestamp_ms", IdlToken.TimestampMs),
        new IdlKeyword("timestamp_micros", IdlToken.TimestampMicros),
        new IdlKeyword("local_timestamp_ms", IdlToken.LocalTimestampMs),
        new IdlKeyword("local_timestamp_micros", IdlToken.LocalTimestampMicros),
        new IdlKeyword("decimal", IdlToken.Decimal),
        new IdlKeyword("duration", IdlToken.Duration),
        new IdlKeyword("uuid", IdlToken.Uuid)
    ];
}