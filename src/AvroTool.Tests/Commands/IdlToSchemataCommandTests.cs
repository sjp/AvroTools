using NUnit.Framework;
using SJP.Avro.AvroTool.Commands;

namespace SJP.Avro.AvroTool.Tests.Commands
{
    [TestFixture]
    internal static class IdlToSchemataCommandTests
    {
        [Test]
        public static void Ctor_WhenInvoked_ConstructsWithoutError()
        {
            Assert.That(() => new IdlToSchemataCommand(), Throws.Nothing);
        }
    }
}
