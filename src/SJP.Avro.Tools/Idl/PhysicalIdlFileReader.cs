using System.IO;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// Reads imported IDL, protocol and schema files from the file system.
/// </summary>
public sealed class PhysicalIdlFileReader : IIdlFileReader
{
    /// <inheritdoc />
    public Stream OpenRead(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"File not found: {path}", path);

        return File.OpenRead(path);
    }
}
