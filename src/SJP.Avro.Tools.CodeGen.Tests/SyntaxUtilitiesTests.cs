using System;
using NUnit.Framework;

namespace SJP.Avro.Tools.CodeGen.Tests;

[TestFixture]
internal static class SyntaxUtilitiesTests
{
    [TestCase("Test.Avro.Namespace", "Test.Avro.Namespace")]
    [TestCase("avro.test.enum", "avro.test.@enum")]
    [TestCase("avro.fixed.enum.test", "avro.@fixed.@enum.test")]
    [TestCase("@enum", "@enum")]
    [TestCase("class", "@class")]
    [TestCase("avro.test.record", "avro.test.record")]
    public static void SafeNamespaceName_GivenNamespace_EscapesOnlyKeywordSegments(string input, string expected)
    {
        Assert.That(SyntaxUtilities.SafeNamespaceName(input).ToFullString(), Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("    ")]
    [TestCase("avro..test")]
    public static void SafeNamespaceName_GivenMissingOrEmptySegment_ThrowsArgumentException(string input)
    {
        Assert.That(() => SyntaxUtilities.SafeNamespaceName(input), Throws.InstanceOf<ArgumentException>());
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    [TestCase("\n\n")]
    [TestCase("*")]
    [TestCase("*\n*\n*")]
    public static void BuildCommentTrivia_GivenCommentWithoutText_ReturnsNoTrivia(string comment)
    {
        Assert.That(SyntaxUtilities.BuildCommentTrivia(comment), Is.Empty);
    }

    [Test]
    public static void BuildCommentTrivia_GivenSingleLineComment_ReturnsSummaryContainingComment()
    {
        var trivia = SyntaxUtilities.BuildCommentTrivia("A test comment.").ToFullString();

        Assert.That(trivia, Does.Contain("<summary>").And.Contain("A test comment.").And.Contain("</summary>"));
    }

    [Test]
    public static void BuildCommentTrivia_GivenCommentWithXmlSyntax_EscapesXmlSyntax()
    {
        var trivia = SyntaxUtilities.BuildCommentTrivia("a < b & c ]]> d").ToFullString();

        Assert.That(trivia, Does.Contain("a &lt; b &amp; c ]]&gt; d"));
    }

    [Test]
    public static void BuildCommentTrivia_GivenMultipleParagraphs_ReturnsParagraphPerBlankSeparatedBlock()
    {
        var trivia = SyntaxUtilities.BuildCommentTrivia("first\n \nsecond").ToFullString();

        Assert.Multiple(() =>
        {
            Assert.That(trivia, Does.Contain("<para>first</para>"));
            Assert.That(trivia, Does.Contain("<para>second</para>"));
        });
    }
}
