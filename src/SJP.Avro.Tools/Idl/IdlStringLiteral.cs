using System;
using System.Globalization;
using System.Text;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// Contains helper methods for handling string literals used within IDL documents.
/// </summary>
public static class IdlStringLiteral
{
    /// <summary>
    /// Converts the text of a string literal to the string value it denotes.
    /// The surrounding quotes are removed and any escape sequences are decoded.
    /// </summary>
    /// <param name="literalText">The text of a string literal, as it appears in an IDL document.</param>
    /// <returns>The value denoted by <paramref name="literalText"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="literalText"/> is <c>null</c>.</exception>
    public static string Unescape(string literalText)
    {
        ArgumentNullException.ThrowIfNull(literalText);

        const char QuoteChar = '"';
        var text = literalText.Length >= 2 && literalText[0] == QuoteChar && literalText[^1] == QuoteChar
            ? literalText[1..^1]
            : literalText;

        if (!text.Contains('\\'))
            return text;

        var builder = new StringBuilder(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '\\' || i == text.Length - 1)
            {
                builder.Append(c);
                continue;
            }

            var escaped = text[++i];
            switch (escaped)
            {
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 'b':
                    builder.Append('\b');
                    break;
                case 'f':
                    builder.Append('\f');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case '\\':
                case '\'':
                case '"':
                    builder.Append(escaped);
                    break;
                case 'u' when TryReadUnicodeEscape(text, i + 1, out var codeUnit):
                    builder.Append(codeUnit);
                    i += 4;
                    break;
                case >= '0' and <= '7':
                    builder.Append(ReadOctalEscape(text, ref i));
                    break;
                default:
                    // not a recognised escape, keep it as written
                    builder.Append('\\').Append(escaped);
                    break;
            }
        }

        return builder.ToString();
    }

    private static bool TryReadUnicodeEscape(string text, int start, out char codeUnit)
    {
        const int HexDigitCount = 4;
        if (start + HexDigitCount > text.Length)
        {
            codeUnit = default;
            return false;
        }

        var digits = text.AsSpan(start, HexDigitCount);
        if (!ushort.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
        {
            codeUnit = default;
            return false;
        }

        codeUnit = (char)value;
        return true;
    }

    /// <summary>
    /// Reads an octal escape starting at the digit at <paramref name="index"/>, advancing
    /// <paramref name="index"/> to the last digit consumed. An escape may hold three digits
    /// only when the first is <c>0</c>-<c>3</c>, keeping the value within a single byte.
    /// </summary>
    private static char ReadOctalEscape(string text, ref int index)
    {
        var maxDigits = text[index] <= '3' ? 3 : 2;

        var value = 0;
        var digitCount = 0;
        while (digitCount < maxDigits && index + digitCount < text.Length && IsOctalDigit(text[index + digitCount]))
        {
            value = (value * 8) + (text[index + digitCount] - '0');
            digitCount++;
        }

        index += digitCount - 1;
        return (char)value;
    }

    private static bool IsOctalDigit(char c) => c is >= '0' and <= '7';
}
