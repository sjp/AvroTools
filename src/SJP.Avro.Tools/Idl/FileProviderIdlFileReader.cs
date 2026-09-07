using System;
using System.IO;
using Microsoft.Extensions.FileProviders;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// Reads imported IDL, protocol and schema files from an <see cref="IFileProvider"/>.
/// </summary>
/// <remarks>
/// The paths given to a file provider are relative to its root, so this reader is intended for
/// documents whose imports are looked up by a bare name, such as embedded resources.
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
