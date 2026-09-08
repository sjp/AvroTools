using System;
using System.IO;
using System.Threading;
using NUnit.Framework;

namespace AvroTool.Tests;

[TestFixture]
internal sealed class OutputCollectorTests
{
    [Test]
    public void WriteAsync_GivenACancelledToken_ThrowsAndLeavesNoTemporaryFileBehind()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.DirectoryPath, "output.avsc");

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            async () => await OutputCollector.WriteAsync(path, "{}", cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(Directory.GetFiles(directory.DirectoryPath), Is.Empty);
    }

    [Test]
    public void WriteAsync_GivenTheDestinationCannotBeReplaced_DeletesTheTemporaryFileItWrote()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.DirectoryPath, "output.avsc");

        // A directory in the way of the destination lets the temporary file be written in full
        // before the final move fails, so cleanup after a genuine mid-operation failure is
        // exercised rather than a write that never started.
        Directory.CreateDirectory(path);

        // File.Move onto an existing directory raises IOException on Windows but
        // UnauthorizedAccessException on Linux/macOS; either signals the same failure.
        Assert.That(
            async () => await OutputCollector.WriteAsync(path, "{}", CancellationToken.None),
            Throws.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
        Assert.That(Directory.GetFileSystemEntries(directory.DirectoryPath), Is.EqualTo([path]));
    }
}
