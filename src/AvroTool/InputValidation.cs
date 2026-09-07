using System.Collections.Generic;
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
    /// Validates that standard input is the only input: a run that also names files has asked
    /// for two different documents, and answering about either one silently would hide the
    /// mistake.
    /// </summary>
    /// <param name="tokens">The positional input arguments.</param>
    /// <param name="description">How the positional input is named in the error, as the start of
    /// a sentence (e.g. <c>"A schema file"</c>).</param>
    public static ValidationResult ValidateStandardInputAlone(IReadOnlyList<string> tokens, string description)
    {
        if (tokens.Any(t => !string.IsNullOrWhiteSpace(t)))
            return ValidationResult.Error($"{description} may not be given together with --stdin.");

        return ValidationResult.Success();
    }

    /// <inheritdoc cref="ValidateStandardInputAlone(IReadOnlyList{string}, string)" />
    public static ValidationResult ValidateStandardInputAlone(string token, string description) =>
        ValidateStandardInputAlone([token], description);

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
