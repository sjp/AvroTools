using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AvroTool;

/// <summary>
/// The standard streams a command reads its input from and writes its payload to.
/// </summary>
/// <remarks>
/// Status and diagnostic messages go through the injected <see cref="IStatusConsole"/>;
/// this is the payload channel alongside it. Commands take it as a dependency rather than reaching
/// for <see cref="Console"/> so that a caller — notably a test — can supply its own streams without
/// mutating process-global state.
/// </remarks>
internal interface IStandardStreams
{
    /// <summary>
    /// The writer that command payloads are written to.
    /// </summary>
    TextWriter Output { get; }

    /// <summary>
    /// Reads all textual content from standard input, or from the given file path.
    /// </summary>
    /// <param name="useStandardInput">When <c>true</c>, read from standard input and ignore <paramref name="path"/>.</param>
    /// <param name="path">A file path to read when not reading from standard input.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The full textual content of the input.</returns>
    Task<string> ReadAllTextAsync(bool useStandardInput, string? path, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a binary stream over standard input, or the given file path.
    /// </summary>
    /// <param name="useStandardInput">When <c>true</c>, read from standard input and ignore <paramref name="path"/>.</param>
    /// <param name="path">A file path to open when not reading from standard input.</param>
    /// <returns>A readable stream over the requested input.</returns>
    Stream OpenRead(bool useStandardInput, string? path);
}

/// <summary>
/// The standard streams of the running process.
/// </summary>
/// <remarks>
/// Both directions are UTF-8 regardless of the platform's console code page, so that a schema
/// written by an editor on one machine reads the same on another, and so that a non-ASCII
/// <c>doc</c> string or record value survives being written to a pipe or a file. Text read from
/// standard input has any byte-order mark removed, matching what
/// <see cref="File.ReadAllTextAsync(string, CancellationToken)"/> does for the same bytes in a
/// file; a leading <c>U+FEFF</c> left in place is not valid JSON or IDL and fails every parse.
/// Text written to standard output carries no byte-order mark of its own.
/// </remarks>
internal sealed class ConsoleStandardStreams : IStandardStreams, IDisposable
{
    /// <summary>
    /// UTF-8 without a byte-order mark, and lenient about malformed bytes: input that is not
    /// valid UTF-8 is decoded to replacement characters and reported by the parser as the
    /// syntax error it is, rather than throwing out of the read.
    /// </summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Func<Stream> _openStandardInput;
    private readonly Lazy<StreamWriter> _output;
    private bool _disposed;

    /// <summary>
    /// Creates the standard streams of the running process.
    /// </summary>
    public ConsoleStandardStreams()
        : this(Console.OpenStandardInput, Console.OpenStandardOutput)
    {
    }

    /// <summary>
    /// Creates standard streams over the given underlying streams.
    /// </summary>
    /// <param name="openStandardInput">Opens the stream that standard input is read from.</param>
    /// <param name="openStandardOutput">Opens the stream that standard output is written to.</param>
    public ConsoleStandardStreams(Func<Stream> openStandardInput, Func<Stream> openStandardOutput)
    {
        ArgumentNullException.ThrowIfNull(openStandardInput);
        ArgumentNullException.ThrowIfNull(openStandardOutput);

        _openStandardInput = openStandardInput;

        // Opened on first use so that a command which writes nothing — help, version, a
        // validation failure — never touches standard output at all.
        _output = new Lazy<StreamWriter>(() => new StreamWriter(openStandardOutput(), Utf8NoBom) { AutoFlush = false });
    }

    /// <inheritdoc />
    /// <remarks>
    /// The writer does not flush as it goes, so it is held for the lifetime of the process and
    /// flushed when these streams are disposed. Left auto-flushing, a command that emits one
    /// line per record costs a write syscall per record.
    /// </remarks>
    public TextWriter Output => _output.Value;

    /// <inheritdoc />
    public async Task<string> ReadAllTextAsync(bool useStandardInput, string? path, CancellationToken cancellationToken)
    {
        if (!useStandardInput)
            return await File.ReadAllTextAsync(path!, cancellationToken).ConfigureAwait(false);

        using var reader = new StreamReader(_openStandardInput(), Utf8NoBom, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Stream OpenRead(bool useStandardInput, string? path) =>
        useStandardInput ? _openStandardInput() : File.OpenRead(path!);

    /// <summary>
    /// Flushes anything a command has written to standard output.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_output.IsValueCreated)
            _output.Value.Dispose();
    }
}
