using NUnit.Framework;

namespace AvroTool.Tests;

[TestFixture]
internal static class NamingConventionsTests
{
    internal enum SampleKind
    {
        Simple,
        ReaderFieldMissingDefaultValue,
    }

    [TestCase(SampleKind.Simple, "SIMPLE")]
    [TestCase(SampleKind.ReaderFieldMissingDefaultValue, "READER_FIELD_MISSING_DEFAULT_VALUE")]
    public static void ToUpperSnake_GivenEnumMember_ReturnsUpperSnakeCase(SampleKind kind, string expected)
    {
        Assert.That(NamingConventions.ToUpperSnake(kind), Is.EqualTo(expected));
    }

    [TestCase("sha-256", "sha256")]
    [TestCase("SHA_256", "sha256")]
    [TestCase("  Backward-Transitive  ", "backwardtransitive")]
    [TestCase("", "")]
    [TestCase(null, "")]
    public static void NormaliseOption_GivenVaryingSpellings_ReturnsCanonicalForm(string? value, string expected)
    {
        Assert.That(NamingConventions.NormaliseOption(value), Is.EqualTo(expected));
    }
}
