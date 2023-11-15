using Avro;
using NUnit.Framework;

namespace SJP.Avro.Tools.CodeGen.Tests;

[TestFixture]
internal static class CodeGeneratorResolverTests
{
    [Test]
    public static void Resolve_GivenValidTypes_ResolvesToNonNullGenerators()
    {
        var resolver = new CodeGeneratorResolver();

        Assert.Multiple(() =>
        {
            var enumGenerator = resolver.Resolve<EnumSchema>();
            Assert.That(enumGenerator, Is.Not.Null);
            Assert.That(enumGenerator, Is.InstanceOf<ICodeGenerator<EnumSchema>>());

            var fixedGenerator = resolver.Resolve<FixedSchema>();
            Assert.That(fixedGenerator, Is.Not.Null);
            Assert.That(fixedGenerator, Is.InstanceOf<ICodeGenerator<FixedSchema>>());

            var recordGenerator = resolver.Resolve<RecordSchema>();
            Assert.That(recordGenerator, Is.Not.Null);
            Assert.That(recordGenerator, Is.InstanceOf<ICodeGenerator<RecordSchema>>());

            var protocolGenerator = resolver.Resolve<Protocol>();
            Assert.That(protocolGenerator, Is.Not.Null);
            Assert.That(protocolGenerator, Is.InstanceOf<ICodeGenerator<Protocol>>());
        });
    }

    [Test]
    public static void Resolve_GivenUnknownType_ResolvesToNullGenerator()
    {
        var resolver = new CodeGeneratorResolver();

        var stringResolver = resolver.Resolve<string>();
        Assert.That(stringResolver, Is.Null);
    }
}