using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

namespace AvroTool;

/// <summary>
/// One output file that an input will produce, and how to produce the content it will hold.
/// </summary>
/// <param name="Path">The full path of the file to be written.</param>
/// <param name="Description">
/// A human-readable description of the part of the input that produces the file,
/// e.g. <c>protocol 'Foo'</c>, used when reporting a clash between two outputs.
/// </param>
/// <param name="Identity">
/// Everything about the input that determines the content: producing an output twice from the
/// same identity yields the same text, so a path already claimed under this identity does not
/// need producing a second time.
/// </param>
/// <param name="ProduceContent">
/// Produces the content the file will hold. Called at most once, and only when the content is
/// actually needed; returns <c>null</c> or whitespace when the input turns out to have nothing
/// to write to the path after all.
/// </param>
internal readonly record struct OutputReservation(string Path, string Description, string Identity, Func<string?> ProduceContent)
{
    /// <summary>
    /// An output whose content is already in hand, and so is cheap enough to be its own identity.
    /// </summary>
    public OutputReservation(string path, string description, string content)
        : this(path, description, content, () => content)
    {
    }
}

/// <summary>
/// One output of an input, and what is to be done with it.
/// </summary>
/// <param name="Path">The full path of the file.</param>
/// <param name="Content">
/// The content to write, or <c>null</c> when an earlier input already produced the file and this
/// input never had to produce it.
/// </param>
/// <param name="AlreadyGeneratedFrom">
/// The earlier input that produced this exact file, or <c>null</c> when this input writes it.
/// </param>
internal readonly record struct PlannedOutput(string Path, string? Content, string? AlreadyGeneratedFrom);

/// <summary>
/// What is to become of one input's outputs: the files to write, or the reason the input
/// cannot be generated.
/// </summary>
/// <param name="Error">The reason the input's outputs are unusable, or <c>null</c> when they are.</param>
/// <param name="Outputs">The input's outputs in order, each either to be written or already produced by an earlier input.</param>
internal sealed record OutputPlan(string? Error, IReadOnlyList<PlannedOutput> Outputs)
{
    public static OutputPlan Failed(string error) => new(error, []);

    public static OutputPlan Planned(IReadOnlyList<PlannedOutput> outputs) => new(null, outputs);
}

/// <summary>
/// Tracks the output files produced across a multi-input run so that the same output path
/// generated twice — whether by one input or by two different inputs — is handled once and
/// deliberately, and honours the <c>--overwrite</c> semantics against files already present
/// on disk.
/// </summary>
/// <remarks>
/// Two inputs commonly produce the same file for a good reason: a type shared through an import
/// belongs to every document that imports it. Where the content matches, the file is written by
/// the first input to claim it and the rest is left alone, so a whole tree of documents can be
/// processed in one run. Only a path claimed twice with differing content is a genuine clash,
/// where writing either version would misrepresent one of the inputs.
/// </remarks>
internal sealed class OutputCollector
{
    /// <summary>
    /// An output path already claimed, and by what. Both the content and the identity of the
    /// input behind it are held as hashes so that a run over a large tree does not keep every
    /// generated file in memory.
    /// </summary>
    private sealed record Claim(string Source, byte[] ContentHash, byte[] IdentityHash)
    {
        public bool CameFrom(string identity) => IdentityHash.AsSpan().SequenceEqual(HashOf(identity));

        public bool Holds(string content) => ContentHash.AsSpan().SequenceEqual(HashOf(content));
    }

    /// <summary>
    /// One requested output, holding on to its content once produced so that a path examined more
    /// than once is never produced twice.
    /// </summary>
    private sealed class PendingOutput(OutputReservation reservation)
    {
        private string? _content;
        private bool _produced;

        public string Path => reservation.Path;

        public string Description => reservation.Description;

        public string Identity => reservation.Identity;

        /// <summary>The content the file will hold, produced on first use.</summary>
        public string? Content
        {
            get
            {
                if (!_produced)
                {
                    _content = reservation.ProduceContent();
                    _produced = true;
                }

                return _content;
            }
        }
    }

    private readonly StringComparer _pathComparer;
    private readonly Dictionary<string, Claim> _claims;
    private readonly bool _overwrite;

    public OutputCollector(bool overwrite, StringComparer pathComparer)
    {
        _overwrite = overwrite;
        _pathComparer = pathComparer;
        _claims = new Dictionary<string, Claim>(pathComparer);
    }

    /// <summary>
    /// Determines whether the file system holding a directory treats file names as
    /// case-sensitive, by writing a probe file and checking whether it can also be found under
    /// a differently-cased name. This is the trait that actually matters for output-path
    /// collisions — unlike the operating system, it varies by volume, notably on case-sensitive
    /// APFS volumes on macOS and case-insensitive mounts on Linux.
    /// </summary>
    /// <param name="directory">The directory to probe. Must already exist.</param>
    /// <returns>
    /// A comparer matching the directory's case sensitivity, or the platform's usual default
    /// when the directory could not be probed.
    /// </returns>
    public static StringComparer DetectPathComparer(DirectoryInfo directory)
    {
        var defaultComparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

        try
        {
            var probePath = Path.Combine(directory.FullName, Path.GetRandomFileName());
            var upper = probePath.ToUpperInvariant();
            var lower = probePath.ToLowerInvariant();
            if (upper == lower)
                return defaultComparer;

            File.WriteAllBytes(probePath, []);
            try
            {
                var variantPath = probePath == upper ? lower : upper;
                return File.Exists(variantPath) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            }
            finally
            {
                File.Delete(probePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return defaultComparer;
        }
    }

    /// <summary>
    /// Reserves all output paths that a single input will produce, atomically: either every path
    /// is accounted for, or nothing is claimed and the reason is reported.
    /// </summary>
    /// <param name="outputs">The files the input produces.</param>
    /// <param name="source">The input the files come from.</param>
    /// <returns>
    /// A plan naming the files to write and those an earlier input already produced identically;
    /// or the first conflict — two outputs of this input disagreeing over a path, an earlier input
    /// having written something different there, or an existing file when <c>--overwrite</c> is
    /// not set.
    /// </returns>
    public OutputPlan Reserve(IReadOnlyList<OutputReservation> outputs, string source)
    {
        var planned = new List<PlannedOutput>(outputs.Count);
        var claimedHere = new Dictionary<string, PendingOutput>(_pathComparer);
        var toClaim = new List<PendingOutput>();

        foreach (var reservation in outputs)
        {
            var output = new PendingOutput(reservation);

            if (claimedHere.TryGetValue(output.Path, out var sibling))
            {
                // The same file described twice within one input: write it once. Two descriptions
                // of the same thing cannot disagree, so neither has to be produced to find out.
                if (string.Equals(sibling.Identity, output.Identity, StringComparison.Ordinal))
                    continue;

                if (!string.Equals(sibling.Content, output.Content, StringComparison.Ordinal))
                    return OutputPlan.Failed($"{sibling.Description} and {output.Description} disagree over the content of '{output.Path}'.");

                continue;
            }

            if (_claims.TryGetValue(output.Path, out var claim))
            {
                // An earlier input already produced this file from the very same thing — a type
                // shared through an import, typically — so its content is known to match without
                // being produced again. Only a different input behind the same path has to be
                // produced, to tell a genuine clash from two routes to the same output.
                if (!claim.CameFrom(output.Identity))
                {
                    var content = output.Content;
                    if (string.IsNullOrWhiteSpace(content))
                        continue;

                    if (!claim.Holds(content))
                        return OutputPlan.Failed($"'{output.Path}' was already generated from '{claim.Source}', with different content.");
                }

                claimedHere[output.Path] = output;
                planned.Add(new PlannedOutput(output.Path, null, claim.Source));
                continue;
            }

            if (string.IsNullOrWhiteSpace(output.Content))
                continue;

            claimedHere[output.Path] = output;
            toClaim.Add(output);
            planned.Add(new PlannedOutput(output.Path, output.Content, null));
        }

        if (!_overwrite)
        {
            var existing = toClaim.Select(static o => o.Path).Where(File.Exists).ToList();
            if (existing.Count > 0)
                return OutputPlan.Failed($"one or more output files already exist ({string.Join(", ", existing)}). Consider using the 'overwrite' option.");
        }

        foreach (var output in toClaim)
            _claims[output.Path] = new Claim(source, HashOf(output.Content!), HashOf(output.Identity));

        return OutputPlan.Planned(planned);
    }

    /// <summary>
    /// Ensures that the directory generated files will be written to exists, creating it —
    /// along with any missing parent directories — when it does not.
    /// </summary>
    /// <returns>
    /// <c>null</c> on success; otherwise a message explaining why the directory is unusable.
    /// </returns>
    public static string? EnsureDirectory(DirectoryInfo directory)
    {
        try
        {
            Directory.CreateDirectory(directory.FullName);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return $"The output directory '{directory.FullName}' could not be created: {ex.Message}";
        }
    }

    /// <summary>
    /// Carries out a plan, writing each file the input produces and reporting what was written
    /// and what an earlier input had already produced.
    /// </summary>
    public static async Task WritePlanAsync(OutputPlan plan, IStatusConsole console, CancellationToken cancellationToken)
    {
        foreach (var planned in plan.Outputs)
        {
            if (planned.AlreadyGeneratedFrom is { } owner)
            {
                console.MarkupLineInterpolated($"[grey]Skipped {planned.Path}, already generated from '{owner}'[/]");
                continue;
            }

            await WriteAsync(planned.Path, planned.Content!, cancellationToken).ConfigureAwait(false);
            console.MarkupLineInterpolated($"[green]Generated {planned.Path}[/]");
        }
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

    private static byte[] HashOf(string content) => SHA256.HashData(Encoding.UTF8.GetBytes(content));
}
