using System;
using System.Collections.Generic;
using Antlr4.Runtime;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// Extensions for common operations on ANTLR-parsed tokens.
/// </summary>
public static class IdlAntlrTokenExtensions
{
    // longest first, so that a comment lined up under '**' is not read as one lined up under '*'
    private static readonly string[] StarPrefixes = ["**", "*"];

    /// <summary>
    /// The name that an identifier stands for, with any escaping removed.
    /// Any part of a dotted identifier may be wrapped in <c>`</c> characters so that a word which
    /// would otherwise be read as a keyword can be used as a name; the backticks are not part of
    /// the name itself.
    /// </summary>
    /// <param name="identifier">An identifier from a parsed IDL document.</param>
    /// <returns>The name the identifier stands for.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="identifier"/> is <c>null</c>.</exception>
    public static string GetName(this IdlParser.IdentifierContext identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return IdlName.Unescape(identifier.GetText());
    }

    /// <summary>
    /// Extracts documentation text from a doc comment token.
    /// </summary>
    /// <remarks>
    /// The comment delimiters are removed, along with the prefix every line shares: either the
    /// leading <c>*</c> or <c>**</c> that lines a comment up under its opening delimiter, or, when
    /// there is none, the indentation common to every line after the first. Only the shared prefix
    /// goes, so indentation relative to it - a code sample, a nested list - survives, as does a
    /// <c>*</c> that is content rather than decoration.
    /// </remarks>
    /// <param name="docToken">A doc comment token, or <c>null</c>.</param>
    /// <returns>The documentation the comment holds, or <c>null</c> when it holds none.</returns>
    public static string? ExtractDocumentation(this IToken? docToken)
    {
        var text = docToken?.Text;
        if (string.IsNullOrWhiteSpace(text))
            return null;

        const string DocCommentPrefix = "/**";
        const string DocCommentSuffix = "*/";
        if (text.StartsWith(DocCommentPrefix, StringComparison.Ordinal))
            text = text[DocCommentPrefix.Length..];
        if (text.EndsWith(DocCommentSuffix, StringComparison.Ordinal))
            text = text[..^DocCommentSuffix.Length];

        var result = StripCommonPrefix(SplitLines(text.Trim()));
        return !string.IsNullOrWhiteSpace(result)
            ? result
            : null;
    }

    /// <summary>
    /// Removes the prefix that every line of a doc comment shares, so that what remains is the
    /// documentation as it was laid out, without the decoration that aligned it in the source.
    /// </summary>
    private static string StripCommonPrefix(List<string> lines)
    {
        // a run of asterisks lining each line up under the opening '/**', optionally followed by a
        // single space separating it from the text
        foreach (var stars in StarPrefixes)
        {
            if (!IsStarAligned(lines, stars))
                continue;

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i].TrimStart()[stars.Length..];
                lines[i] = line.Length > 0 && char.IsWhiteSpace(line[0])
                    ? line[1..]
                    : line;
            }

            return string.Join('\n', lines);
        }

        // failing that, the indentation shared by every line after the first, which the opening
        // delimiter has already stripped from the first
        var indent = CommonIndent(lines);
        if (indent.Length > 0)
        {
            for (var i = 1; i < lines.Count; i++)
                lines[i] = lines[i][indent.Length..];
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Whether the first line opens with the given run of asterisks and every later line does too,
    /// once its indentation is discounted.
    /// </summary>
    private static bool IsStarAligned(List<string> lines, string stars)
    {
        if (!lines[0].StartsWith(stars, StringComparison.Ordinal))
            return false;

        for (var i = 1; i < lines.Count; i++)
        {
            if (!lines[i].TrimStart().StartsWith(stars, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    /// <summary>
    /// The longest indentation that every line after the first begins with, taken from the second
    /// line. The first line is discounted because the opening delimiter, not whitespace, precedes it.
    /// </summary>
    private static string CommonIndent(List<string> lines)
    {
        if (lines.Count < 2)
            return string.Empty;

        var indent = lines[1].AsSpan()[..(lines[1].Length - lines[1].TrimStart().Length)];

        for (var i = 2; i < lines.Count; i++)
        {
            while (indent.Length > 0 && !lines[i].AsSpan().StartsWith(indent, StringComparison.Ordinal))
                indent = indent[..^1];
        }

        return new string(indent);
    }

    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        foreach (var line in text.EnumerateLines())
            lines.Add(new string(line));

        return lines;
    }
}
