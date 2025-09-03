using Superpower.Model;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// A tokenizer for Avro IDL documents.
/// </summary>
public interface IIdlTokenizer
{
    /// <summary>
    /// Tokenize source
    /// </summary>
    /// <param name="source">The source to tokenize.</param>
    /// <returns>The list of tokens or an error.</returns>
    TokenList<IdlToken> Tokenize(string source);

    /// <summary>
    /// Tokenize source.
    /// </summary>
    /// <param name="source">The source to tokenize.</param>
    /// <returns>A result with the list of tokens or an error.</returns>
    Result<TokenList<IdlToken>> TryTokenize(string source);
}
