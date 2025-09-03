using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace SJP.Avro.Tools.Tests;

[TestFixture]
internal static class StringExtensionsTests
{
    [Test]
    public static void Join_GivenNullStringCollection_ThrowsArgumentNullException()
    {
        IEnumerable<string> values = null;
        Assert.That(() => values.Join(","), Throws.ArgumentNullException);
    }

    [Test]
    public static void Join_GivenNullSeparator_ThrowsArgumentNullException()
    {
        var values = Array.Empty<string>();
        Assert.That(() => values.Join(null), Throws.ArgumentNullException);
    }

    [Test]
    public static void Join_GivenSingleString_ReturnsInput()
    {
        var values = new[] { "test" };
        var result = values.Join(",");

        Assert.That(result, Is.EqualTo(values[0]));
    }

    [Test]
    public static void Join_GivenManyStringsWithNonEmptySeparator_ReturnsStringSeparatedBySeparator()
    {
        const string expectedResult = "test1,test2,test3";
        var values = new[] { "test1", "test2", "test3" };
        var result = values.Join(",");

        Assert.That(result, Is.EqualTo(expectedResult));
    }

    [Test]
    public static void Join_GivenManyStringsWithEmptySeparator_ReturnsStringsConcatenated()
    {
        const string expectedResult = "test1test2test3";
        var values = new[] { "test1", "test2", "test3" };
        var result = values.Join(string.Empty);

        Assert.That(result, Is.EqualTo(expectedResult));
    }
}