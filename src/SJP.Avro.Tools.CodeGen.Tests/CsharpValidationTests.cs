using System;
using System.Diagnostics;
using NUnit.Framework;

namespace SJP.Avro.Tools.CodeGen.Tests;

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
    [TestCase("class", false)]
    [TestCase("class.int", false)]
    [TestCase("Foo.class", false)]
    [TestCase("@class", true)]
    [TestCase("@class.Foo", true)]
    [TestCase("Foo.@int", true)]
    [TestCase("@", false)]
    [TestCase("@@class", false)]
    [TestCase("@Foo", true)]
    [TestCase("record.var", true)]
    [TestCase("Ünïcödé.Идентификатор", true)]
    [TestCase("A!", false)]
    public static void IsValidCsharpNamespace_GivenVariousInputs_ReturnsExpectedResult(string? input, bool expected)
    {
        var result = CsharpValidation.IsValidCsharpNamespace(input!);

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("   ", false)]
    [TestCase("A", true)]
    [TestCase("_A", true)]
    [TestCase("A1", true)]
    [TestCase("1A", false)]
    [TestCase("A B", false)]
    [TestCase("A.B", false)]
    [TestCase("my-ns", false)]
    [TestCase("foo-bar", false)]
    [TestCase("A!", false)]
    [TestCase("Ünïcödé", true)]
    // A keyword is escaped where it is emitted, so it is usable as it stands.
    [TestCase("class", true)]
    [TestCase("record", true)]
    // An escape is not something an Avro name may carry in the first place.
    [TestCase("@class", false)]
    [TestCase("@", false)]
    public static void IsValidCsharpIdentifier_GivenVariousInputs_ReturnsExpectedResult(string? input, bool expected)
    {
        var result = CsharpValidation.IsValidCsharpIdentifier(input!);

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public static void IsValidCsharpNamespace_GivenLongInvalidInput_CompletesQuickly()
    {
        var input = new string('a', 2000) + "!";

        var stopwatch = Stopwatch.StartNew();
        var result = CsharpValidation.IsValidCsharpNamespace(input);
        stopwatch.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)));
        });
    }
}
