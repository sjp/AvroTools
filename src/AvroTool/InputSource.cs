using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AvroTool;

/// <summary>
/// Resolves a command's input to its textual content, supporting reading from
/// standard input so the tool composes in shell pipelines.
/// </summary>
/// <remarks>
/// A bare "-" would be the conventional stdin token, but Spectre.Console.Cli's
/// argument tokenizer rejects it ("Option does not have a name"), so commands
/// opt in via an explicit <c>--stdin</c> flag instead.
/// </remarks>
internal static class InputSource
{
    /// <summary>
    /// Reads all textual content from standard input, or from the given file path.
    /// </summary>
    /// <param name="useStandardInput">When <c>true</c>, read from standard input and ignore <paramref name="path"/>.</param>
    /// <param name="path">A file path to read when not reading from standard input.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The full textual content of the input.</returns>
    public static async Task<string> ReadAllTextAsync(bool useStandardInput, string? path, CancellationToken cancellationToken)
    {
        if (useStandardInput)
            return await Console.In.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        return await File.ReadAllTextAsync(path!, cancellationToken).ConfigureAwait(false);
    }

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

    /// <summary>
    /// Opens a binary stream over standard input, or the given file path.
    /// </summary>
    /// <param name="useStandardInput">When <c>true</c>, read from standard input and ignore <paramref name="path"/>.</param>
    /// <param name="path">A file path to open when not reading from standard input.</param>
    /// <returns>A readable stream over the requested input.</returns>
    public static Stream OpenRead(bool useStandardInput, string? path) =>
        useStandardInput ? Console.OpenStandardInput() : File.OpenRead(path!);
}
