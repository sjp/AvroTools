using System;
using System.IO;

namespace SJP.Avro.Tools.Tests;

/// <summary>
/// A temporary directory that will be deleted once disposed.
/// </summary>
/// <seealso cref="IDisposable" />
internal sealed class TemporaryDirectory : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TemporaryDirectory"/> class.
    /// </summary>
    public TemporaryDirectory()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(DirectoryPath);
    }

    /// <summary>
    /// The directory path of the temporary directory, always a random location.
    /// </summary>
    /// <value>The directory path.</value>
    public string DirectoryPath { get; }

    /// <summary>
    /// Writes a file relative to the temporary directory, creating any intermediate directories.
    /// </summary>
    /// <param name="relativePath">The path of the file, relative to the temporary directory.</param>
    /// <param name="content">The content to write.</param>
    /// <returns>The full path of the written file.</returns>
    public string WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(DirectoryPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);

        return fullPath;
    }

    /// <summary>
    /// Deletes the temporary directory, including all of its contents.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        Directory.Delete(DirectoryPath, true);
        _disposed = true;
    }

    private bool _disposed;
}
