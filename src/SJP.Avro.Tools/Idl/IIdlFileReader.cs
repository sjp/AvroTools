using System.IO;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// Reads the contents of files referenced by IDL <c>import</c> statements.
/// </summary>
public interface IIdlFileReader
{
    /// <summary>
    /// Opens the file at the given path for reading.
    /// </summary>
    /// <param name="path">The path of the file to read. This is a fully qualified path whenever the
    /// location of the importing document is known, and the literal import path otherwise.</param>
    /// <returns>A stream containing the contents of the file.</returns>
    /// <exception cref="FileNotFoundException">No file exists at <paramref name="path"/>.</exception>
    Stream OpenRead(string path);
}
