using System;
using System.IO;

namespace AvroTool.Tests;

/// <summary>
/// A file that exists but cannot be opened for reading while this object is held, so a test can
/// exercise the path where an input is present yet unreadable — a permission denied, a lock held
/// by another process, or anything else that fails at the point of opening it.
/// </summary>
/// <seealso cref="IDisposable" />
internal sealed class UnreadableFile : IDisposable
{
    private readonly FileStream _exclusiveHandle;
    private bool _disposed;

    /// <summary>
    /// Creates the file at the given path and holds it open exclusively.
    /// </summary>
    /// <param name="path">The path of the file to create.</param>
    public UnreadableFile(string path)
    {
        Path = path;
        _exclusiveHandle = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
    }

    /// <summary>
    /// The path of the unreadable file.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Releases the file, making it readable again.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _exclusiveHandle.Dispose();
        _disposed = true;
    }
}
