using System.Text.RegularExpressions;

namespace SJP.Avro.Tools;

/// <summary>
/// Helpers for working with validation of C# code
/// </summary>
public static partial class CsharpValidation
{
    /// <summary>
    /// A regular expression that can be used to validate a C# namespace.
    /// </summary>
    private static readonly Regex CsharpNamespacePattern = CsharpNamespaceRegex();

    /// <summary>
    /// Determines whether the input string is a valid C# namespace.
    /// </summary>
    /// <param name="input">A string to test.</param>
    /// <returns><c>true</c> if the input string is a valid C# namespace; otherwise, <c>false</c>.</returns>
    public static bool IsValidCsharpNamespace(string input)
    {
        return !string.IsNullOrWhiteSpace(input)
            && CsharpNamespacePattern.IsMatch(input);
    }

    [GeneratedRegex("^([a-zA-Z_][a-zA-Z0-9_]*)+([.][a-zA-Z_][a-zA-Z0-9_]*)*$", RegexOptions.Compiled)]
    private static partial Regex CsharpNamespaceRegex();
}