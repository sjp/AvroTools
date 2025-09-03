using System;
using System.IO;

namespace SJP.Avro.Tools;

/// <summary>
/// A file provider that operates with the physical file system.
/// </summary>
public class DefaultFileProvider : IFileProvider
{
    /// <summary>
    /// Constructs a path that is relative to a base path.
    /// </summary>
    /// <param name="basePath">A base path that is used to construct a full path.</param>
    /// <param name="relativePath">A path that is relative to the given base path.</param>
    /// <returns>A string representing a file path.</returns>
    /// <example>a base path of '/a/b/c.avdl' and a relative path of 'test.avdl' will return '/a/b/test.avdl'.</example>
    /// <exception cref="ArgumentNullException"><paramref name="basePath"/> or <paramref name="relativePath"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="basePath"/> or <paramref name="relativePath"/> is empty or whitespace.</exception>
    public string CreatePath(string basePath, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var dir = Path.GetDirectoryName(basePath) ?? basePath;
        return Path.Combine(dir, relativePath);
    }

    /// <summary>
    /// Retrieves the contents of a file at a described location.
    /// </summary>
    /// <param name="basePath">A base path that is used to construct a full path.</param>
    /// <param name="relativePath">A path that is relative to the given base path.</param>
    /// <returns>The contents of a file relative to the given base path.</returns>
    /// <example>a base path of '/a/b/c.avdl' and a relative path of 'test.avdl' will return the contents of '/a/b/test.avdl'.</example>
    /// <exception cref="ArgumentException"><paramref name="basePath"/> or <paramref name="relativePath"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="basePath"/> or <paramref name="relativePath"/> is empty or whitespace.</exception>
    public string GetFileContents(string basePath, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var resolvedPath = CreatePath(basePath, relativePath);
        return File.ReadAllText(resolvedPath);
    }
}