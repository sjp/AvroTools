using NUnit.Framework;
using SJP.Avro.AvroTool.Commands;

namespace SJP.Avro.AvroTool.Tests.Commands;

[TestFixture]
internal static class CodeGenCommandTests
{
    [Test]
    public static void Ctor_WhenInvoked_ConstructsWithoutError()
    {
        Assert.That(() => new CodeGenCommand(), Throws.Nothing);
    }
}
