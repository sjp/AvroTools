using AvroTool.Commands;
using NUnit.Framework;

namespace AvroTool.Tests.Commands;

[TestFixture]
internal static class CodeGenCommandTests
{
    [Test]
    public static void Ctor_WhenInvoked_ConstructsWithoutError()
    {
        Assert.That(() => new CodeGenCommand(), Throws.Nothing);
    }
}