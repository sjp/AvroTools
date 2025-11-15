using System;
using System.IO;
using Antlr4.Runtime;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// Error listener that throws on syntax errors.
/// </summary>
public class ThrowingErrorListener : BaseErrorListener
{
    /// <summary>
    /// Method called when a syntax error is encountered.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when a syntax error has occurred.</exception>
    public override void SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        IToken offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e)
    {
        throw new InvalidOperationException(
            $"Syntax error at line {line}:{charPositionInLine} - {msg}",
            e);
    }
}
