using System;
using System.Text;

namespace AvroTool;

/// <summary>
/// Shared spelling conventions for option values read from, and enum names written to, the console.
/// </summary>
internal static class NamingConventions
{
    /// <summary>
    /// Renders an enumeration member in the UPPER_SNAKE_CASE convention shared with the Java tooling.
    /// </summary>
    public static string ToUpperSnake<TEnum>(TEnum value) where TEnum : struct, Enum =>
        ToUpperSnake(value.ToString());

    /// <summary>
    /// Renders a PascalCase name in UPPER_SNAKE_CASE.
    /// </summary>
    public static string ToUpperSnake(string name)
    {
        var builder = new StringBuilder(name.Length + 6);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
                builder.Append('_');
            builder.Append(char.ToUpperInvariant(name[i]));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reduces a user-supplied option value to a canonical form so that hyphen-, underscore- and
    /// case-varying spellings (e.g. <c>sha-256</c>, <c>SHA_256</c>) all match the same entry.
    /// </summary>
    public static string NormaliseOption(string? value) =>
        (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Replace("-", string.Empty)
            .Replace("_", string.Empty);
}
