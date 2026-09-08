using System;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// The error raised when an IDL document cannot be translated, whether because it could not be
/// tokenised or parsed, or because what it declares cannot be expressed as an Avro protocol or
/// schema.
/// </summary>
/// <remarks>
/// Where the position of the offending declaration is known it is carried alongside the message,
/// so that a consumer can report it in whatever form suits, rather than having to read it back out
/// of the message text.
/// </remarks>
public sealed class IdlTranslationException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IdlTranslationException"/> class.
    /// </summary>
    public IdlTranslationException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IdlTranslationException"/> class.
    /// </summary>
    /// <param name="message">A description of what could not be translated.</param>
    public IdlTranslationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IdlTranslationException"/> class.
    /// </summary>
    /// <param name="message">A description of what could not be translated.</param>
    /// <param name="innerException">The error that caused this one, when there is one.</param>
    public IdlTranslationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IdlTranslationException"/> class, describing
    /// where in a document the error was found.
    /// </summary>
    /// <param name="message">A description of what could not be translated.</param>
    /// <param name="fileName">The document the error was found in, or <c>null</c> when it was found in the document handed to the translator.</param>
    /// <param name="lineNumber">The one-based line the error was found on, or <c>null</c> when no line is known.</param>
    /// <param name="columnNumber">The zero-based character position within the line, or <c>null</c> when no position is known.</param>
    /// <param name="innerException">The error that caused this one, when there is one.</param>
    public IdlTranslationException(string message, string? fileName, int? lineNumber, int? columnNumber, Exception? innerException = null)
        : base(message, innerException)
    {
        FileName = fileName;
        LineNumber = lineNumber;
        ColumnNumber = columnNumber;
    }

    /// <summary>
    /// The document the error was found in. <c>null</c> when the error was found in the document
    /// handed to the translator rather than in one reached through an <c>import</c>.
    /// </summary>
    public string? FileName { get; }

    /// <summary>
    /// The one-based line the error was found on, or <c>null</c> when no line is known.
    /// </summary>
    public int? LineNumber { get; }

    /// <summary>
    /// The zero-based character position within <see cref="LineNumber"/>, or <c>null</c> when no
    /// position is known.
    /// </summary>
    public int? ColumnNumber { get; }
}
