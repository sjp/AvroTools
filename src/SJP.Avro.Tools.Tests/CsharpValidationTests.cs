using NUnit.Framework;

namespace SJP.Avro.Tools.Tests;

[TestFixture]
internal static class CsharpValidationTests
{
    [Test]
    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("   ", false)]
    [TestCase("A", true)]
    [TestCase("A.B", true)]
    [TestCase("A.B.C", true)]
    [TestCase("_A", true)]
    [TestCase("A1", true)]
    [TestCase("1A", false)]
    [TestCase("A B", false)]
    [TestCase("A.1B", false)]
    [TestCase("A..B", false)]
    [TestCase(".A", false)]
    [TestCase("A.", false)]
    [TestCase("SJP.Avro.Tools", true)]
    public static void IsValidCsharpNamespace_GivenVariousInputs_ReturnsExpectedResult(string input, bool expected)
    {
        var result = CsharpValidation.IsValidCsharpNamespace(input);

        Assert.That(result, Is.EqualTo(expected));
    }
}