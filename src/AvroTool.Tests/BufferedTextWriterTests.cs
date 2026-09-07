using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace AvroTool.Tests;

[TestFixture]
internal class BufferedTextWriterTests
{
    [Test]
    public void Write_GivenTextSmallerThanTheBuffer_DoesNotForwardUntilFlushed()
    {
        var inner = new StringWriter();
        using var writer = new BufferedTextWriter(inner, bufferSize: 64);

        writer.Write("abc");

        Assert.That(inner.ToString(), Is.Empty);

        writer.Flush();

        Assert.That(inner.ToString(), Is.EqualTo("abc"));
    }

    [Test]
    public void Write_GivenMoreTextThanTheBufferHolds_ForwardsWithoutAFlush()
    {
        var inner = new StringWriter();
        using var writer = new BufferedTextWriter(inner, bufferSize: 4);

        writer.Write("abcdef");

        Assert.That(inner.ToString(), Is.Not.Empty);
    }

    [Test]
    public void Dispose_GivenBufferedText_ForwardsWhatRemains()
    {
        var inner = new StringWriter();
        using (var writer = new BufferedTextWriter(inner, bufferSize: 64))
        {
            writer.Write("abc");
        }

        Assert.That(inner.ToString(), Is.EqualTo("abc"));
    }

    [Test]
    public async Task WriteLineAsync_GivenManyLines_PreservesEveryLineInOrder()
    {
        const int lineCount = 5_000;

        var inner = new StringWriter();
        await using (var writer = new BufferedTextWriter(inner, bufferSize: 16))
        {
            for (var i = 0; i < lineCount; i++)
                await writer.WriteLineAsync(i.ToString(CultureInfo.InvariantCulture).AsMemory(), TestContext.CurrentContext.CancellationToken);
        }

        var lines = inner.ToString().TrimEnd().ReplaceLineEndings("\n").Split('\n');
        using (Assert.EnterMultipleScope())
        {
            Assert.That(lines, Has.Length.EqualTo(lineCount));
            Assert.That(lines[0], Is.EqualTo("0"));
            Assert.That(lines[^1], Is.EqualTo((lineCount - 1).ToString(CultureInfo.InvariantCulture)));
        }
    }
}
