using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AvroTool;

/// <summary>
/// A <see cref="TextWriter"/> that accumulates text and forwards it to another writer in
/// large blocks rather than as it arrives.
/// </summary>
/// <remarks>
/// A command that emits one line per record costs a write call per record — and a terminal
/// redraw per record when the output is not redirected — which dominates the run time of a
/// large object container file. Wrapping the destination in this writer reduces that to one
/// write per buffer-full, whatever buffering the destination does for itself. The inner writer
/// is not owned, and so is flushed but never disposed.
/// </remarks>
internal sealed class BufferedTextWriter : TextWriter
{
    private const int DefaultBufferSize = 64 * 1024;

    private readonly TextWriter _inner;
    private readonly char[] _buffer;
    private int _count;

    /// <summary>
    /// Wraps a writer so that text written here reaches it in blocks.
    /// </summary>
    /// <param name="inner">The writer that buffered text is forwarded to.</param>
    /// <param name="bufferSize">The number of characters to accumulate before forwarding.</param>
    public BufferedTextWriter(TextWriter inner, int bufferSize = DefaultBufferSize)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfLessThan(bufferSize, 1);

        _inner = inner;
        _buffer = new char[bufferSize];
        NewLine = inner.NewLine;
    }

    /// <inheritdoc />
    public override Encoding Encoding => _inner.Encoding;

    /// <inheritdoc />
    public override IFormatProvider FormatProvider => _inner.FormatProvider;

    /// <inheritdoc />
    public override void Write(char value)
    {
        if (_count == _buffer.Length)
            Drain();

        _buffer[_count++] = value;
    }

    /// <inheritdoc />
    public override void Write(string? value)
    {
        if (value != null)
            Write(value.AsSpan());
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<char> buffer)
    {
        // Text that cannot fit in the buffer at all is passed straight through instead of
        // being copied in chunks.
        if (buffer.Length >= _buffer.Length)
        {
            Drain();
            _inner.Write(buffer);
            return;
        }

        if (buffer.Length > _buffer.Length - _count)
            Drain();

        buffer.CopyTo(_buffer.AsSpan(_count));
        _count += buffer.Length;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Buffering is a memory copy, so the asynchronous overloads complete synchronously rather
    /// than queueing work — the base class implementations would start a task per call.
    /// </remarks>
    public override Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);

        Write(buffer.Span);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);

        WriteLine(buffer.Span);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override void Flush()
    {
        Drain();
        _inner.Flush();
    }

    /// <inheritdoc />
    public override Task FlushAsync()
    {
        Flush();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Forwards whatever has accumulated so far, without flushing the inner writer.
    /// </summary>
    private void Drain()
    {
        if (_count == 0)
            return;

        var pending = _count;

        // Cleared before the write so that a failing inner writer cannot cause the same text
        // to be written again by a later drain (notably the one during disposal).
        _count = 0;
        _inner.Write(_buffer.AsSpan(0, pending));
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Flush();

        base.Dispose(disposing);
    }
}
