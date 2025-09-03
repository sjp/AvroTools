namespace SJP.Avro.Tools;

/// <summary>
/// Provides access to files from a physical (or virtual) file system.
/// </summary>
public interface IFileProvider
{
    /// <summary>
    /// Retrieves the contents of a file at a described location.
    /// </summary>
    /// <param name="basePath">A base path that is used to construct a full path.</param>
    /// <param name="relativePath">A path that is relative to the given base path.</param>
    /// <returns>The contents of a file relative to the given base path.</returns>
    string GetFileContents(string basePath, string relativePath);

    /// <summary>
    /// Constructs a path that is relative to a base path.
    /// </summary>
    /// <param name="basePath">A base path that is used to construct a full path.</param>
    /// <param name="relativePath">A path that is relative to the given base path.</param>
    /// <returns>A string representing a file path.</returns>
    string CreatePath(string basePath, string relativePath);
}