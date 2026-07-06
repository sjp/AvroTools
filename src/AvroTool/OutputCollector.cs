using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AvroTool;

/// <summary>
/// Tracks the output files produced across a multi-input run so that the same
/// output path generated from two different inputs is detected and reported
/// (rather than silently racing), and honours the <c>--overwrite</c> semantics
/// against files already present on disk.
/// </summary>
internal sealed class OutputCollector
{
    private readonly Dictionary<string, string> _claims = new(StringComparer.Ordinal);
    private readonly bool _overwrite;

    public OutputCollector(bool overwrite) => _overwrite = overwrite;

    /// <summary>
    /// Reserves all output paths that a single input will produce, atomically.
    /// </summary>
    /// <returns>
    /// <c>null</c> on success; otherwise an error message describing the first conflict — either a
    /// duplicate output shared with an earlier input, or an existing file when <c>--overwrite</c> is not set.
    /// </returns>
    public string? Reserve(IReadOnlyList<string> paths, string source)
    {
        foreach (var path in paths)
        {
            if (_claims.TryGetValue(path, out var owner))
                return $"'{path}' would be generated from both '{owner}' and '{source}'.";
        }

        if (!_overwrite)
        {
            var existing = paths.Where(File.Exists).ToList();
            if (existing.Count > 0)
                return $"one or more output files already exist ({string.Join(", ", existing)}). Consider using the 'overwrite' option.";
        }

        foreach (var path in paths)
            _claims[path] = source;

        return null;
    }

    /// <summary>Writes content to a reserved output path, replacing any existing file.</summary>
    public static async Task WriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
            File.Delete(path);

        await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
    }
}
