using System.IO;

namespace AvroTool;

/// <summary>
/// Describes where a command's input came from, for the parts of that answer which are
/// independent of the streams themselves.
/// </summary>
/// <remarks>
/// A bare "-" would be the conventional stdin token, but Spectre.Console.Cli's
/// argument tokenizer rejects it ("Option does not have a name"), so commands
/// opt in via an explicit <c>--stdin</c> flag instead.
/// </remarks>
internal static class InputSource
{
    /// <summary>
    /// The name used to identify standard input in messages and machine-readable output.
    /// </summary>
    public const string StandardInputName = "<stdin>";

    /// <summary>
    /// The directory that a document's relative import paths resolve against.
    /// </summary>
    /// <param name="useStandardInput">When <c>true</c>, the document came from standard input and <paramref name="path"/> is ignored.</param>
    /// <param name="path">The path of the file the document was read from.</param>
    /// <returns>The directory holding the file, or the current directory for standard input.</returns>
    public static string ImportBaseDirectory(bool useStandardInput, string? path)
    {
        if (useStandardInput || string.IsNullOrEmpty(path))
            return Directory.GetCurrentDirectory();

        return Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory();
    }
}
