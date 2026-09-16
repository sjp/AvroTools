namespace SJP.Avro.Tools.Idl;

/// <summary>
/// Keeps the parse of an imported IDL document so that documents importing the same file do not
/// each pay for reading and parsing it.
/// </summary>
/// <remarks>
/// A cache holds what a file contained when it was first read, so its lifetime should not outlast
/// the run of work it was created for. Only the parse is kept; the translation of an import still
/// runs for each document importing it, because its output is relative to the importing document.
/// </remarks>
public interface IIdlImportCache
{
    /// <summary>
    /// Retrieves the parse of a document that has already been read, if there is one.
    /// </summary>
    /// <param name="path">The resolved path the document was imported from.</param>
    /// <param name="document">The parsed document, or <c>null</c> when none has been kept for <paramref name="path"/>.</param>
    /// <returns><see langword="true"/> if a parsed document was found, otherwise <see langword="false"/>.</returns>
    bool TryGetDocument(string path, out ParsedIdlDocument? document);

    /// <summary>
    /// Keeps the parse of a document, so that a later import of the same path can reuse it.
    /// </summary>
    /// <param name="path">The resolved path the document was imported from.</param>
    /// <param name="document">The parsed document.</param>
    void AddDocument(string path, ParsedIdlDocument document);
}
