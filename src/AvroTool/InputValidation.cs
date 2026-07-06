using System.IO;
using System.Linq;
using Spectre.Console;

namespace AvroTool;

/// <summary>
/// Shared up-front validation for the file-consuming commands' input arguments.
/// Directories and glob patterns are resolved later (at execution time); this only
/// gives an early, clear error for an explicitly named path that does not exist.
/// </summary>
internal static class InputValidation
{
    private static readonly char[] WildcardChars = ['*', '?', '['];

    /// <summary>
    /// Validates the raw input tokens. <paramref name="noun"/> is woven into the
    /// error messages (e.g. <c>"IDL"</c> → "An IDL file must be provided.").
    /// </summary>
    public static ValidationResult Validate(string[] tokens, string noun)
    {
        var meaningful = tokens.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (meaningful.Count == 0)
            return ValidationResult.Error($"An {noun} file must be provided.");

        foreach (var token in meaningful)
        {
            var isGlob = token.IndexOfAny(WildcardChars) >= 0;
            if (!isGlob && !File.Exists(token) && !Directory.Exists(token))
                return ValidationResult.Error($"An {noun} file could not be found at: {token}");
        }

        return ValidationResult.Success();
    }
}
