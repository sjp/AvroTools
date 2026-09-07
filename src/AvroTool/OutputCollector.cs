using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AvroTool;

/// <summary>
/// One output file that an input will produce.
/// </summary>
/// <param name="Path">The full path of the file to be written.</param>
/// <param name="Description">
/// A human-readable description of the part of the input that produces the file,
/// e.g. <c>protocol 'Foo'</c>, used when reporting a clash between two outputs.
/// </param>
internal readonly record struct OutputReservation(string Path, string Description);

/// <summary>
/// Tracks the output files produced across a multi-input run so that the same
/// output path generated twice — whether by one input or by two different inputs —
/// is detected and reported (rather than silently racing), and honours the
/// <c>--overwrite</c> semantics against files already present on disk.
/// </summary>
internal sealed class OutputCollector
{
    /// <summary>
    /// Compares output paths the way the file system does: case-sensitively on Linux,
    /// case-insensitively on Windows and macOS.
    /// </summary>
    private static readonly StringComparer PathComparer = OperatingSystem.IsLinux()
        ? StringComparer.Ordinal
        : StringComparer.OrdinalIgnoreCase;

    private readonly Dictionary<string, string> _claims = new(PathComparer);
    private readonly bool _overwrite;

    public OutputCollector(bool overwrite) => _overwrite = overwrite;

    /// <summary>
    /// Reserves all output paths that a single input will produce, atomically.
    /// </summary>
    /// <returns>
    /// <c>null</c> on success; otherwise an error message describing the first conflict — either two
    /// outputs of this input sharing a path, a duplicate output shared with an earlier input, or an
    /// existing file when <c>--overwrite</c> is not set.
    /// </returns>
    public string? Reserve(IReadOnlyList<OutputReservation> outputs, string source)
    {
        var claimedHere = new Dictionary<string, string>(PathComparer);
        foreach (var output in outputs)
        {
            if (claimedHere.TryGetValue(output.Path, out var sibling))
                return $"'{output.Path}' would be generated from both {sibling} and {output.Description}.";

            claimedHere[output.Path] = output.Description;
        }

        foreach (var output in outputs)
        {
            if (_claims.TryGetValue(output.Path, out var owner))
                return $"'{output.Path}' would be generated from both '{owner}' and '{source}'.";
        }

        if (!_overwrite)
        {
            var existing = outputs.Select(o => o.Path).Where(File.Exists).ToList();
            if (existing.Count > 0)
                return $"one or more output files already exist ({string.Join(", ", existing)}). Consider using the 'overwrite' option.";
        }

        foreach (var output in outputs)
            _claims[output.Path] = source;

        return null;
    }

    /// <summary>
    /// Writes content to a reserved output path, replacing any existing file. The content is
    /// written to a temporary file alongside the destination and then moved into place, so an
    /// interrupted write never leaves a missing or half-written output.
    /// </summary>
    public static async Task WriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        var tempPath = Path.Combine(directory ?? string.Empty, Path.GetRandomFileName());

        try
        {
            await File.WriteAllTextAsync(tempPath, content, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                    // best-effort cleanup, the failed write is what matters
                }
                catch (UnauthorizedAccessException)
                {
                    // best-effort cleanup, the failed write is what matters
                }
            }
        }
    }
}
