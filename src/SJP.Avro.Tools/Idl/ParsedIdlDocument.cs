namespace SJP.Avro.Tools.Idl;

/// <summary>
/// An IDL document that has been read and parsed, ready to be translated.
/// </summary>
/// <remarks>
/// The parse of a document is immutable and independent of the document importing it, so one
/// parse can be translated any number of times. Nothing of it is exposed, because it is only
/// ever handed back to the translator that created it, by an <see cref="IIdlImportCache"/>.
/// </remarks>
public sealed class ParsedIdlDocument
{
    internal ParsedIdlDocument(IdlParser.IdlFileContext tree, IdlDocComments docComments)
    {
        Tree = tree;
        DocComments = docComments;
    }

    /// <summary>
    /// The parse tree of the document.
    /// </summary>
    internal IdlParser.IdlFileContext Tree { get; }

    /// <summary>
    /// The documentation written against the document's declarations. Doc comments are tokenised
    /// onto a hidden channel, so they are resolved from the token stream rather than read out of
    /// the parse tree.
    /// </summary>
    internal IdlDocComments DocComments { get; }
}
