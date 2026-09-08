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
}
