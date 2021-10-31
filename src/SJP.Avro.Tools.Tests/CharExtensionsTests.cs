using NUnit.Framework;

namespace SJP.Avro.Tools.Tests
{
    [TestFixture]
    internal static class CharExtensionsTests
    {
        [Test]
        public static void IsDigit_GivenDigit_ReturnsTrue()
        {
            Assert.That('1'.IsDigit(), Is.True);
        }

        [Test]
        public static void IsDigit_GivenNonDigit_ReturnsFalse()
        {
            Assert.That('a'.IsDigit(), Is.False);
        }

        [Test]
        public static void IsLetter_GivenLetter_ReturnsTrue()
        {
            Assert.That('a'.IsLetter(), Is.True);
        }

        [Test]
        public static void IsLetter_GivenNonLetter_ReturnsFalse()
        {
            Assert.That('1'.IsLetter(), Is.False);
        }

        [Test]
        public static void IsLetterOrDigit_GivenLetter_ReturnsTrue()
        {
            Assert.That('a'.IsLetterOrDigit(), Is.True);
        }

        [Test]
        public static void IsLetterOrDigit_GivenDigit_ReturnsTrue()
        {
            Assert.That('1'.IsLetterOrDigit(), Is.True);
        }

        [Test]
        public static void IsLetterOrDigit_GivenNonDigitOrLetter_ReturnsFalse()
        {
            Assert.That('_'.IsLetterOrDigit(), Is.False);
        }
    }
}
