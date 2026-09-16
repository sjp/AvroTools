using System;
using System.Collections.Concurrent;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// An <see cref="IIdlImportCache"/> that keeps every document it is given in memory, for as long
/// as the cache itself is held. Create one per run of work, so that a document is never read from
/// a file that has since changed.
/// </summary>
public sealed class IdlImportCache : IIdlImportCache
{
    private readonly ConcurrentDictionary<string, ParsedIdlDocument> _documents = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public bool TryGetDocument(string path, out ParsedIdlDocument? document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return _documents.TryGetValue(path, out document);
    }

    /// <inheritdoc/>
    public void AddDocument(string path, ParsedIdlDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);

        _documents[path] = document;
    }
}
