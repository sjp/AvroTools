using System;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

namespace AvroTool;

/// <summary>
/// Reads a command's input, turning a file that cannot be opened or read into a reported
/// failure rather than an exception that ends the process.
/// </summary>
/// <remarks>
/// An input can be unreadable for reasons that have nothing to do with its content — a
/// permission denied, a directory in place of a file, a lock held by another process — and
/// discovering that is part of processing an input, not a fault in the run as a whole. Kept
/// alongside the parse failures the commands already report, one bad file costs its own error
/// line while the remaining inputs are still processed.
/// </remarks>
internal static class InputReader
{
    /// <summary>
    /// Reads the full text of an input, reporting a read failure to the console.
    /// </summary>
    /// <param name="streams">The streams to read through.</param>
    /// <param name="useStandardInput">When <c>true</c>, read standard input and ignore <paramref name="path"/>.</param>
    /// <param name="path">The path of the file to read.</param>
    /// <param name="displayName">The name of the input to use when reporting a failure.</param>
    /// <param name="console">The console to write a failure to.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The content of the input, or <c>null</c> when it could not be read or was empty.</returns>
    public static async Task<string?> TryReadAllTextAsync(
        IStandardStreams streams,
        bool useStandardInput,
        string? path,
        string displayName,
        IStatusConsole console,
        CancellationToken cancellationToken)
    {
        string content;
        try
        {
            content = await streams.ReadAllTextAsync(useStandardInput, path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            console.MarkupLineInterpolated($"[red]Unable to read '{displayName}': {ex.Message}[/]");
            return null;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            var subject = useStandardInput ? "standard input" : $"'{displayName}'";
            console.MarkupLineInterpolated($"[red]{subject} was empty[/]");
            return null;
        }

        return content;
    }

    /// <summary>
    /// Opens a binary stream over an input, reporting a failure to open it to the console.
    /// </summary>
    /// <param name="streams">The streams to read through.</param>
    /// <param name="useStandardInput">When <c>true</c>, open standard input and ignore <paramref name="path"/>.</param>
    /// <param name="path">The path of the file to open.</param>
    /// <param name="displayName">The name of the input to use when reporting a failure.</param>
    /// <param name="console">The console to write a failure to.</param>
    /// <returns>A readable stream over the input, or <c>null</c> when it could not be opened.</returns>
    public static Stream? TryOpenRead(
        IStandardStreams streams,
        bool useStandardInput,
        string? path,
        string displayName,
        IStatusConsole console)
    {
        try
        {
            return streams.OpenRead(useStandardInput, path);
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            console.MarkupLineInterpolated($"[red]Unable to read '{displayName}': {ex.Message}[/]");
            return null;
        }
    }

    /// <summary>
    /// Whether an exception describes an input that could not be read, as opposed to a failure
    /// that leaves the process in no state to carry on with the remaining inputs. Cancellation
    /// is none of these, so a cancelled run still ends rather than being reported per input.
    /// </summary>
    private static bool IsReadFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException or SecurityException;
}
