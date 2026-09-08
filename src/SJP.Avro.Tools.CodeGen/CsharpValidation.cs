using Microsoft.CodeAnalysis.CSharp;

namespace SJP.Avro.Tools.CodeGen;

/// <summary>
/// Helpers for working with validation of C# code
/// </summary>
public static class CsharpValidation
{
    /// <summary>
    /// Determines whether the input string is a valid C# namespace.
    /// </summary>
    /// <param name="input">A string to test.</param>
    /// <returns><c>true</c> if the input string is a valid C# namespace; otherwise, <c>false</c>.</returns>
    public static bool IsValidCsharpNamespace(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        foreach (var segment in input.Split('.'))
        {
            if (!IsValidNamespaceSegment(segment))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Determines whether a name declared by an Avro definition can be used as a C# identifier as
    /// it stands. A keyword is not an obstacle here, unlike in a namespace supplied by hand:
    /// generated code escapes a name that is one with a leading <c>@</c>. A character C# does not
    /// admit in an identifier at all has no such remedy.
    /// </summary>
    /// <param name="input">A string to test.</param>
    /// <returns><c>true</c> if the input string can be used as a C# identifier; otherwise, <c>false</c>.</returns>
    public static bool IsValidCsharpIdentifier(string input)
    {
        return !string.IsNullOrWhiteSpace(input)
            && SyntaxFacts.IsValidIdentifier(input);
    }

    /// <summary>
    /// Determines whether one dot-separated piece of a namespace is a legal C# identifier. Keywords
    /// are only permitted when they are escaped with a leading <c>@</c>.
    /// </summary>
    private static bool IsValidNamespaceSegment(string segment)
    {
        var escaped = segment.StartsWith('@');
        var name = escaped ? segment[1..] : segment;

        if (!SyntaxFacts.IsValidIdentifier(name))
            return false;

        return escaped || SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None;
    }
}
