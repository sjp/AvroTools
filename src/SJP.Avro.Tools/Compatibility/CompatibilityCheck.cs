using System;
using Avro;

namespace SJP.Avro.Tools.Compatibility;

/// <summary>
/// One reader/writer comparison made while checking a set of schemas under a compatibility mode,
/// together with the direction it was made for and where each schema sat in the input.
/// </summary>
public sealed class CompatibilityCheck
{
    /// <summary>
    /// Initialises a new instance of the <see cref="CompatibilityCheck"/> class.
    /// </summary>
    /// <param name="direction">The direction this comparison covers.</param>
    /// <param name="readerIndex">The position of the reader schema in the checked set.</param>
    /// <param name="writerIndex">The position of the writer schema in the checked set.</param>
    /// <param name="reader">The schema acting as the reader.</param>
    /// <param name="writer">The schema acting as the writer.</param>
    /// <param name="result">The outcome of comparing the two.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/>, <paramref name="writer"/> or <paramref name="result"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="readerIndex"/> or <paramref name="writerIndex"/> is negative.</exception>
    public CompatibilityCheck(
        CompatibilityDirection direction,
        int readerIndex,
        int writerIndex,
        Schema reader,
        Schema writer,
        SchemaCompatibilityResult result)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentOutOfRangeException.ThrowIfNegative(readerIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(writerIndex);

        Direction = direction;
        ReaderIndex = readerIndex;
        WriterIndex = writerIndex;
        Reader = reader;
        Writer = writer;
        Result = result;
    }

    /// <summary>The direction this comparison covers.</summary>
    public CompatibilityDirection Direction { get; }

    /// <summary>The position of the reader schema in the checked set.</summary>
    public int ReaderIndex { get; }

    /// <summary>The position of the writer schema in the checked set.</summary>
    public int WriterIndex { get; }

    /// <summary>The schema acting as the reader.</summary>
    public Schema Reader { get; }

    /// <summary>The schema acting as the writer.</summary>
    public Schema Writer { get; }

    /// <summary>The outcome of comparing the two.</summary>
    public SchemaCompatibilityResult Result { get; }
}
