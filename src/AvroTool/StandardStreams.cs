using System;
using System.IO;
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
internal sealed class ConsoleStandardStreams : IStandardStreams
{
    /// <inheritdoc />
    public TextWriter Output => Console.Out;

    /// <inheritdoc />
    public async Task<string> ReadAllTextAsync(bool useStandardInput, string? path, CancellationToken cancellationToken)
    {
        if (useStandardInput)
            return await Console.In.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        return await File.ReadAllTextAsync(path!, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Stream OpenRead(bool useStandardInput, string? path) =>
        useStandardInput ? Console.OpenStandardInput() : File.OpenRead(path!);
}
