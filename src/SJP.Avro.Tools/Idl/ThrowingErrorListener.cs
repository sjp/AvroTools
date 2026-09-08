using System.IO;
using Antlr4.Runtime;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// Error listener that throws on syntax errors, for both lexing and parsing.
/// </summary>
public class ThrowingErrorListener : BaseErrorListener, IAntlrErrorListener<int>
{
    /// <summary>
    /// Method called when a syntax error is encountered while parsing.
    /// </summary>
    /// <exception cref="IdlTranslationException">Thrown when a syntax error has occurred.</exception>
    public override void SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        IToken offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e)
    {
        throw SyntaxErrorException(line, charPositionInLine, msg, e);
    }

    /// <summary>
    /// Method called when a syntax error is encountered while tokenising.
    /// </summary>
    /// <exception cref="IdlTranslationException">Thrown when a syntax error has occurred.</exception>
    public void SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        int offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e)
    {
        throw SyntaxErrorException(line, charPositionInLine, msg, e);
    }

    private static IdlTranslationException SyntaxErrorException(int line, int charPositionInLine, string msg, RecognitionException e)
    {
        return new IdlTranslationException(
            $"Syntax error at line {line}:{charPositionInLine} - {msg}",
            null,
            line,
            charPositionInLine,
            e);
    }
}
