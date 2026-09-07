using System;
using System.Globalization;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// Contains helper methods for handling numeric literals used within IDL documents.
/// </summary>
/// <remarks>
/// IDL numeric literals follow Java's syntax, which permits radix prefixes, a leading
/// sign, and type suffixes that .NET's own parsers do not accept.
/// </remarks>
public static class IdlNumericLiteral
{
    /// <summary>
    /// Converts the text of an integer literal to the value it denotes. Decimal,
    /// hexadecimal (<c>0x</c>-prefixed) and octal (<c>0</c>-prefixed) forms are supported,
    /// as are a leading sign and a trailing <c>l</c> or <c>L</c> suffix.
    /// </summary>
    /// <param name="literalText">The text of an integer literal, as it appears in an IDL document.</param>
    /// <returns>The value denoted by <paramref name="literalText"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="literalText"/> is <c>null</c>.</exception>
    /// <exception cref="FormatException"><paramref name="literalText"/> does not denote a 64-bit integer.</exception>
    public static long ParseInteger(string literalText)
    {
        ArgumentNullException.ThrowIfNull(literalText);

        var text = literalText.Trim();

        var isNegative = text.StartsWith('-');
        if (isNegative || text.StartsWith('+'))
            text = text[1..];

        if (text.EndsWith('l') || text.EndsWith('L'))
            text = text[..^1];

        if (text.Length == 0)
            throw new FormatException($"'{literalText}' is not a valid integer literal.");

        try
        {
            var magnitude = GetRadix(text) switch
            {
                16 => Convert.ToInt64(text[2..], 16),
                8 => Convert.ToInt64(text[1..], 8),
                _ => long.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture)
            };

            return isNegative ? -magnitude : magnitude;
        }
        catch (OverflowException)
        {
            throw new FormatException($"'{literalText}' is outside the range of a 64-bit integer.");
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new FormatException($"'{literalText}' is not a valid integer literal.");
        }
    }

    /// <summary>
    /// Converts the text of an integer literal to the 32-bit value it denotes, in the same
    /// manner as <see cref="ParseInteger(string)"/>.
    /// </summary>
    /// <param name="literalText">The text of an integer literal, as it appears in an IDL document.</param>
    /// <returns>The value denoted by <paramref name="literalText"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="literalText"/> is <c>null</c>.</exception>
    /// <exception cref="FormatException"><paramref name="literalText"/> does not denote a 32-bit integer.</exception>
    public static int ParseInt32(string literalText)
    {
        var value = ParseInteger(literalText);
        if (value is < int.MinValue or > int.MaxValue)
            throw new FormatException($"'{literalText}' is outside the range of a 32-bit integer.");

        return (int)value;
    }

    /// <summary>
    /// Converts the text of a floating point literal to the value it denotes. Decimal and
    /// hexadecimal forms are supported, as are a leading sign, an exponent, a trailing
    /// <c>f</c>/<c>F</c>/<c>d</c>/<c>D</c> suffix, and the <c>NaN</c> and <c>Infinity</c> forms.
    /// </summary>
    /// <param name="literalText">The text of a floating point literal, as it appears in an IDL document.</param>
    /// <returns>The value denoted by <paramref name="literalText"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="literalText"/> is <c>null</c>.</exception>
    /// <exception cref="FormatException"><paramref name="literalText"/> does not denote a floating point value.</exception>
    public static double ParseDouble(string literalText)
    {
        ArgumentNullException.ThrowIfNull(literalText);

        var text = literalText.Trim();

        var isNegative = text.StartsWith('-');
        if (isNegative || text.StartsWith('+'))
            text = text[1..];

        var magnitude = text switch
        {
            "NaN" => double.NaN,
            "Infinity" => double.PositiveInfinity,
            _ => ParseUnsignedDouble(text, literalText)
        };

        return isNegative ? -magnitude : magnitude;
    }

    private static double ParseUnsignedDouble(string text, string literalText)
    {
        // a hexadecimal literal always carries a binary exponent, so a trailing 'd' or 'f'
        // can only ever be a type suffix rather than part of the number
        if (text.Length > 0 && text[^1] is 'f' or 'F' or 'd' or 'D')
            text = text[..^1];

        if (text.Length == 0)
            throw new FormatException($"'{literalText}' is not a valid floating point literal.");

        if (IsHexadecimal(text))
            return ParseHexadecimalDouble(text[2..], literalText);

        const NumberStyles styles = NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;
        if (!double.TryParse(text, styles, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"'{literalText}' is not a valid floating point literal.");

        return value;
    }

    /// <summary>
    /// Converts the digits of a hexadecimal floating point literal, i.e. the text following
    /// the <c>0x</c> prefix, to the value they denote. The mantissa is a hexadecimal
    /// fraction and the exponent is a decimal power of two.
    /// </summary>
    private static double ParseHexadecimalDouble(string digits, string literalText)
    {
        var exponentIndex = digits.IndexOfAny(['p', 'P']);
        if (exponentIndex < 0)
            throw new FormatException($"'{literalText}' is not a valid floating point literal.");

        var mantissaText = digits[..exponentIndex];
        var exponentText = digits[(exponentIndex + 1)..];

        var pointIndex = mantissaText.IndexOf('.', StringComparison.Ordinal);
        var integerDigits = pointIndex < 0 ? mantissaText : mantissaText[..pointIndex];
        var fractionDigits = pointIndex < 0 ? string.Empty : mantissaText[(pointIndex + 1)..];

        if (integerDigits.Length == 0 && fractionDigits.Length == 0)
            throw new FormatException($"'{literalText}' is not a valid floating point literal.");

        var mantissa = 0d;
        foreach (var digit in integerDigits)
            mantissa = (mantissa * 16) + GetHexDigitValue(digit, literalText);

        var scale = 1d;
        foreach (var digit in fractionDigits)
        {
            scale /= 16;
            mantissa += GetHexDigitValue(digit, literalText) * scale;
        }

        const NumberStyles styles = NumberStyles.AllowLeadingSign;
        if (!int.TryParse(exponentText, styles, CultureInfo.InvariantCulture, out var exponent))
            throw new FormatException($"'{literalText}' is not a valid floating point literal.");

        return Math.ScaleB(mantissa, exponent);
    }

    private static int GetHexDigitValue(char digit, string literalText)
    {
        return digit switch
        {
            >= '0' and <= '9' => digit - '0',
            >= 'a' and <= 'f' => digit - 'a' + 10,
            >= 'A' and <= 'F' => digit - 'A' + 10,
            _ => throw new FormatException($"'{literalText}' is not a valid floating point literal.")
        };
    }

    /// <summary>
    /// Determines the radix denoted by the prefix of an unsigned literal. A <c>0x</c>
    /// prefix denotes hexadecimal, a lone leading <c>0</c> denotes octal.
    /// </summary>
    private static int GetRadix(string text)
    {
        if (IsHexadecimal(text))
            return 16;

        if (text.Length > 1 && text[0] == '0')
            return 8;

        return 10;
    }

    private static bool IsHexadecimal(string text) =>
        text.Length > 2 && text[0] == '0' && text[1] is 'x' or 'X';
}
