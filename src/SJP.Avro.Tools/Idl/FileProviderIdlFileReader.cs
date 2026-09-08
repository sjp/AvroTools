using System;
using System.IO;
using Microsoft.Extensions.FileProviders;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// Reads imported IDL, protocol and schema files from an <see cref="IFileProvider"/>.
/// </summary>
/// <remarks>
/// The paths given to a file provider are relative to its root, so an import written in the
/// document handed to the translator is looked up exactly as it is written. An import written in a
/// document that was itself reached through a subdirectory is resolved against that subdirectory,
/// so that a document refers to its neighbours by name wherever it sits under the root.
/// </remarks>
public sealed class FileProviderIdlFileReader : IIdlFileReader
{
    private readonly IFileProvider _fileProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileProviderIdlFileReader"/> class.
    /// </summary>
    /// <param name="fileProvider">The file provider to read imported files from.</param>
    public FileProviderIdlFileReader(IFileProvider fileProvider)
    {
        ArgumentNullException.ThrowIfNull(fileProvider);

        _fileProvider = fileProvider;
    }

    /// <inheritdoc />
    public Stream OpenRead(string path)
    {
        var fileInfo = _fileProvider.GetFileInfo(path);
        if (!fileInfo.Exists)
            throw new FileNotFoundException($"File not found: {path}", path);

        return fileInfo.CreateReadStream();
    }
}
