using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AvroTool.Tests;

/// <summary>
/// In-memory standard streams for command tests: standard output is captured, and standard
/// input is whatever the test supplies. File paths are still read from disk, so a test can
/// mix the two.
/// </summary>
internal sealed class TestStandardStreams : IStandardStreams
{
    private readonly StringWriter _output = new();

    /// <summary>
    /// The text handed to a command that reads standard input as text.
    /// </summary>
    public string StandardInputText { get; set; } = string.Empty;

    /// <summary>
    /// The bytes handed to a command that opens standard input as a binary stream.
    /// </summary>
    public byte[] StandardInputBytes { get; set; } = [];

    /// <inheritdoc />
    public TextWriter Output => _output;

    /// <summary>
    /// Everything written to standard output so far.
    /// </summary>
    public string OutputText => _output.ToString();

    /// <inheritdoc />
    public Task<string> ReadAllTextAsync(bool useStandardInput, string path, CancellationToken cancellationToken)
    {
        if (useStandardInput)
            return Task.FromResult(StandardInputText);

        return File.ReadAllTextAsync(path, cancellationToken);
    }

    /// <inheritdoc />
    public Stream OpenRead(bool useStandardInput, string path) =>
        useStandardInput ? new MemoryStream(StandardInputBytes, writable: false) : File.OpenRead(path);
}
