using System.IO;
using System.Linq;
using NUnit.Framework;

namespace AvroTool.Tests;

[TestFixture]
internal class InputExpanderTests
{
    private static readonly string[] IdlExtensions = [".avdl"];

    private TemporaryDirectory _tempDir;

    [SetUp]
    public void Setup() => _tempDir = new TemporaryDirectory();

    [TearDown]
    public void TearDown() => _tempDir?.Dispose();

    private string Touch(string relativePath)
    {
        var full = Path.Combine(_tempDir.DirectoryPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "// placeholder");
        return full;
    }

    [Test]
    public void Expand_GivenMultipleExplicitFiles_ReturnsAllInOrder()
    {
        var a = Touch("a.avdl");
        var b = Touch("b.avdl");

        var result = InputExpander.Expand([a, b], IdlExtensions, recursive: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Files, Is.EqualTo(new[] { a, b }));
            Assert.That(result.UnmatchedTokens, Is.Empty);
        }
    }

    [Test]
    public void Expand_GivenExplicitFile_IncludesItRegardlessOfExtension()
    {
        var odd = Touch("schema.txt");

        var result = InputExpander.Expand([odd], IdlExtensions, recursive: true);

        Assert.That(result.Files, Is.EqualTo(new[] { odd }));
    }

    [Test]
    public void Expand_GivenDirectory_RecursesByDefaultAndFiltersByExtension()
    {
        var top = Touch("top.avdl");
        var nested = Touch("nested/deep.avdl");
        Touch("ignore.txt");

        var result = InputExpander.Expand([_tempDir.DirectoryPath], IdlExtensions, recursive: true);

        Assert.That(result.Files, Is.EquivalentTo(new[] { top, nested }));
    }

    [Test]
    public void Expand_GivenDirectoryNonRecursive_ReturnsOnlyTopLevel()
    {
        var top = Touch("top.avdl");
        Touch("nested/deep.avdl");

        var result = InputExpander.Expand([_tempDir.DirectoryPath], IdlExtensions, recursive: false);

        Assert.That(result.Files, Is.EqualTo(new[] { top }));
    }

    [Test]
    public void Expand_GivenRecursiveGlob_MatchesNestedFiles()
    {
        var top = Touch("top.avdl");
        var nested = Touch("nested/deep.avdl");
        Touch("nested/other.avsc");

        var glob = Path.Combine(_tempDir.DirectoryPath, "**", "*.avdl");
        var result = InputExpander.Expand([glob], IdlExtensions, recursive: true);

        Assert.That(result.Files, Is.EquivalentTo(new[] { top, nested }));
    }

    [Test]
    public void Expand_GivenOverlappingTokens_DeduplicatesPreservingFirstSeen()
    {
        var a = Touch("a.avdl");

        var result = InputExpander.Expand([a, _tempDir.DirectoryPath], IdlExtensions, recursive: true);

        Assert.That(result.Files, Is.EqualTo(new[] { a }));
    }

    [Test]
    public void Expand_GivenMissingLiteralAndEmptyGlob_ReportsUnmatched()
    {
        var missing = Path.Combine(_tempDir.DirectoryPath, "nope.avdl");
        var emptyGlob = Path.Combine(_tempDir.DirectoryPath, "*.avdl");

        var result = InputExpander.Expand([missing, emptyGlob], IdlExtensions, recursive: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Files, Is.Empty);
            Assert.That(result.UnmatchedTokens, Is.EqualTo(new[] { missing, emptyGlob }));
        }
    }

    [Test]
    public void Expand_IgnoresEmptyTokens()
    {
        var a = Touch("a.avdl");

        var result = InputExpander.Expand(["", "   ", a], IdlExtensions, recursive: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Files, Is.EqualTo(new[] { a }));
            Assert.That(result.UnmatchedTokens, Is.Empty);
        }
    }
}
