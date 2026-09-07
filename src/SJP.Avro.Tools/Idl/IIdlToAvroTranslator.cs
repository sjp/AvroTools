using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// Translates IDL documents to their equivalent JSON-compatible protocol and schema forms.
/// </summary>
public interface IIdlToAvroTranslator
{
    /// <summary>
    /// Translates IDL to either Protocol or Schema.
    /// </summary>
    /// <param name="idlContent">A stream whose contents contain an IDL representing a protocol or a schema.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A parse result that contains either a protocol or a schema. If parsing fails, an exception is thrown.</returns>
    Task<IdlParseResult> Translate(Stream idlContent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Translates IDL to either Protocol or Schema, resolving relative imports against a directory.
    /// </summary>
    /// <param name="idlContent">A stream whose contents contain an IDL representing a protocol or a schema.</param>
    /// <param name="baseDirectory">The directory that relative import paths are resolved against, typically the directory containing the document. When <c>null</c>, import paths are used exactly as written.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A parse result that contains either a protocol or a schema. If parsing fails, an exception is thrown.</returns>
    Task<IdlParseResult> Translate(Stream idlContent, string? baseDirectory, CancellationToken cancellationToken);

    /// <summary>
    /// Translates IDL to either Protocol or Schema.
    /// </summary>
    /// <param name="idlContent">A string containing an IDL representing a protocol or a schema.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A parse result that contains either a protocol or a schema. If parsing fails, an exception is thrown.</returns>
    Task<IdlParseResult> Translate(string idlContent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Translates IDL to either Protocol or Schema, resolving relative imports against a directory.
    /// </summary>
    /// <param name="idlContent">A string containing an IDL representing a protocol or a schema.</param>
    /// <param name="baseDirectory">The directory that relative import paths are resolved against, typically the directory containing the document. When <c>null</c>, import paths are used exactly as written.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A parse result that contains either a protocol or a schema. If parsing fails, an exception is thrown.</returns>
    Task<IdlParseResult> Translate(string idlContent, string? baseDirectory, CancellationToken cancellationToken);
}
