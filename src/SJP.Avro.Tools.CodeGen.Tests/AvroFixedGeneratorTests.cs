using Avro;
using NUnit.Framework;

namespace SJP.Avro.Tools.CodeGen.Tests;

[TestFixture]
internal static class AvroFixedGeneratorTests
{
    private const string TestNamespace = "Test.Avro.Namespace";

    [Test]
    public static void Generate_GivenNullSchema_ThrowsArgumentNullException()
    {
        var fixedGenerator = new AvroFixedGenerator();

        Assert.That(() => fixedGenerator.Generate(default!, TestNamespace), Throws.ArgumentNullException);
    }

    [Test]
    public static void Generate_GivenNullBaseNamespace_ThrowsArgumentNullException()
    {
        var fixedGenerator = new AvroFixedGenerator();

        var schema = Schema.Parse(@"{
    ""type"": ""fixed"",
    ""name"": ""MD5"",
    ""doc"": ""An MD5 hash."",
    ""namespace"": ""org.apache.avro.test"",
    ""size"": 16,
    ""foo"": ""bar""
}") as FixedSchema;

        Assert.That(() => fixedGenerator.Generate(schema, null), Throws.ArgumentNullException);
    }

    [TestCase("")]
    [TestCase("    ")]
    public static void Generate_GivenEmptyOrWhitespaceBaseNamespace_ThrowsArgumentException(string baseNamespace)
    {
        var fixedGenerator = new AvroFixedGenerator();

        var schema = Schema.Parse(@"{
    ""type"": ""fixed"",
    ""name"": ""MD5"",
    ""doc"": ""An MD5 hash."",
    ""namespace"": ""org.apache.avro.test"",
    ""size"": 16,
    ""foo"": ""bar""
}") as FixedSchema;

        Assert.That(() => fixedGenerator.Generate(schema, baseNamespace), Throws.ArgumentException);
    }

    [Test]
    public static void Generate_GivenValidFixedSchema_GeneratesExpectedCode()
    {
        var fixedGenerator = new AvroFixedGenerator();

        var schema = Schema.Parse(@"{
    ""type"": ""fixed"",
    ""name"": ""MD5"",
    ""doc"": ""An MD5 hash."",
    ""namespace"": ""org.apache.avro.test"",
    ""size"": 16,
    ""foo"": ""bar""
}") as FixedSchema;

        var result = fixedGenerator.Generate(schema, TestNamespace);

        const string expected = @"using System;
using System.Collections.Generic;
using Avro;
using Avro.Specific;

namespace org.apache.avro.test
{
    /// <summary>
    /// An MD5 hash.
    /// </summary>
    public record MD5 : SpecificFixed
    {
        private static readonly Schema _schema = Schema.Parse(""{\""type\"":\""fixed\"",\""name\"":\""MD5\"",\""doc\"":\""An MD5 hash.\"",\""namespace\"":\""org.apache.avro.test\"",\""size\"":16,\""foo\"":\""bar\""}"");

        public override Schema Schema { get; } = _schema;

        public static uint FixedSize { get; } = 16;

        public MD5() : base(FixedSize)
        {
        }
    }
}";

        Assert.That(result, Is.EqualTo(expected).IgnoreLineEndingFormat);
    }

    [Test]
    public static void Generate_GivenFixedSchemaWithoutNamespace_GeneratesCodeWithDefaultNamespace()
    {
        var fixedGenerator = new AvroFixedGenerator();

        var schema = Schema.Parse(@"{
    ""type"": ""fixed"",
    ""name"": ""MD5"",
    ""size"": 16,
    ""foo"": ""bar""
}") as FixedSchema;

        var result = fixedGenerator.Generate(schema, TestNamespace);

        const string expected = @$"using System;
using System.Collections.Generic;
using Avro;
using Avro.Specific;

namespace {TestNamespace}
{{
    public record MD5 : SpecificFixed
    {{
        private static readonly Schema _schema = Schema.Parse(""{{\""type\"":\""fixed\"",\""name\"":\""MD5\"",\""size\"":16,\""foo\"":\""bar\""}}"");

        public override Schema Schema {{ get; }} = _schema;

        public static uint FixedSize {{ get; }} = 16;

        public MD5() : base(FixedSize)
        {{
        }}
    }}
}}";

        Assert.That(result, Is.EqualTo(expected).IgnoreLineEndingFormat);
    }
}