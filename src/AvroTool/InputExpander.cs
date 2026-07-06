using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace AvroTool;

/// <summary>
/// The result of expanding a set of raw input tokens into concrete files.
/// </summary>
/// <param name="Files">The de-duplicated, ordered list of matched files (full paths).</param>
/// <param name="UnmatchedTokens">Tokens that matched no file (a missing path, or a glob with no results).</param>
internal sealed record InputExpansion(IReadOnlyList<string> Files, IReadOnlyList<string> UnmatchedTokens);

/// <summary>
/// Expands raw command-line input tokens — explicit files, directories, or glob
/// patterns — into a concrete, de-duplicated list of files to process, so a whole
/// tree of schema/IDL files can be handled in a single invocation.
/// </summary>
internal static class InputExpander
{
    private static readonly char[] WildcardChars = ['*', '?', '['];

    /// <summary>
    /// Expands the given tokens into files, keeping first-seen order and removing duplicates.
    /// </summary>
    /// <param name="tokens">The raw input arguments. Empty/whitespace tokens are ignored.</param>
    /// <param name="extensions">Recognised file extensions (including the leading dot, e.g. <c>.avdl</c>)
    /// used when expanding directories and glob patterns. An explicitly named existing file is always
    /// included regardless of its extension.</param>
    /// <param name="recursive">When <c>true</c>, directory inputs are searched recursively.</param>
    public static InputExpansion Expand(IEnumerable<string> tokens, IReadOnlyCollection<string> extensions, bool recursive)
    {
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unmatched = new List<string>();

        void Add(string path)
        {
            var full = Path.GetFullPath(path);
            if (seen.Add(full))
                files.Add(full);
        }

        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token))
                continue;

            if (File.Exists(token))
            {
                Add(token);
            }
            else if (Directory.Exists(token))
            {
                var matches = EnumerateDirectory(token, extensions, recursive).ToList();
                if (matches.Count == 0)
                    unmatched.Add(token);
                else
                    matches.ForEach(Add);
            }
            else if (token.IndexOfAny(WildcardChars) >= 0)
            {
                var matches = EnumerateGlob(token, extensions).ToList();
                if (matches.Count == 0)
                    unmatched.Add(token);
                else
                    matches.ForEach(Add);
            }
            else
            {
                unmatched.Add(token);
            }
        }

        return new InputExpansion(files, unmatched);
    }

    private static IEnumerable<string> EnumerateDirectory(string directory, IReadOnlyCollection<string> extensions, bool recursive)
    {
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory
            .EnumerateFiles(directory, "*", option)
            .Where(f => HasRecognisedExtension(f, extensions))
            .OrderBy(f => f, StringComparer.Ordinal);
    }

    private static IEnumerable<string> EnumerateGlob(string glob, IReadOnlyCollection<string> extensions)
    {
        var (baseDirectory, pattern) = SplitGlob(glob);
        if (!Directory.Exists(baseDirectory))
            return [];

        var matcher = new Matcher(StringComparison.Ordinal);
        matcher.AddInclude(pattern);

        var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(baseDirectory)));
        return result.Files
            .Select(m => Path.GetFullPath(Path.Combine(baseDirectory, m.Path)))
            .Where(f => HasRecognisedExtension(f, extensions))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Splits a glob into its non-wildcard base directory and the remaining pattern,
    /// which is matched relative to that base.
    /// </summary>
    private static (string BaseDirectory, string Pattern) SplitGlob(string glob)
    {
        var normalized = glob.Replace('\\', '/');
        var wildcardIndex = normalized.IndexOfAny(WildcardChars);
        var slashBeforeWildcard = normalized.LastIndexOf('/', wildcardIndex);

        if (slashBeforeWildcard < 0)
            return (Directory.GetCurrentDirectory(), normalized);

        var basePart = normalized[..slashBeforeWildcard];
        var pattern = normalized[(slashBeforeWildcard + 1)..];

        var baseDirectory = basePart.Length == 0
            ? "/"
            : Path.IsPathRooted(basePart) ? basePart : Path.Combine(Directory.GetCurrentDirectory(), basePart);

        return (baseDirectory, pattern);
    }

    private static bool HasRecognisedExtension(string path, IReadOnlyCollection<string> extensions) =>
        extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
