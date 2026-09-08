using System;
using System.Collections.Generic;
using Antlr4.Runtime;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// Extensions for common operations on ANTLR-parsed tokens.
/// </summary>
public static class IdlAntlrTokenExtensions
{
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
    /// Properly handles multi-line doc comments by removing comment delimiters and leading asterisks.
    /// </summary>
    public static string? ExtractDocumentation(this IToken? docToken)
    {
        var text = docToken?.Text;
        if (string.IsNullOrWhiteSpace(text))
            return null;

        const string DocCommentPrefix = "/**";
        const string DocCommentSuffix = "*/";
        if (text.StartsWith(DocCommentPrefix))
            text = text[DocCommentPrefix.Length..];
        if (text.EndsWith(DocCommentSuffix))
            text = text[..^DocCommentSuffix.Length];

        var cleanedLines = new List<string>();

        foreach (var line in text.EnumerateLines())
        {
            var trimmed = line.Trim();
            // remove leading asterisk if present
            if (trimmed.StartsWith('*'))
                trimmed = trimmed[1..].TrimStart();

            cleanedLines.Add(new string(trimmed));
        }

        // Join lines and trim the result
        var result = string.Join("\n", cleanedLines).Trim();
        return !string.IsNullOrWhiteSpace(result)
            ? result
            : null;
    }
}
