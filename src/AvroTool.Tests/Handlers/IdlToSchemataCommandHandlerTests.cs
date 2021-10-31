using System;
using System.CommandLine;
using System.CommandLine.IO;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SJP.Avro.AvroTool.Handlers;
using SJP.Avro.Tools;
using SJP.Avro.Tools.Idl;

namespace SJP.Avro.AvroTool.Tests.Handlers
{
    [TestFixture]
    internal class IdlToSchemataCommandHandlerTests
    {
        private const string SimpleTestIdl = @"protocol TestProtocol {
  record TestRecord {
    string FirstName;
    string LastName;
  }
}
";

        private TemporaryDirectory _tempDir;
        private Mock<IConsole> _console;
        private IdlToSchemataCommandHandler _commandHandler;

        [SetUp]
        public void Setup()
        {
            _tempDir = new TemporaryDirectory();

            _console = new Mock<IConsole>(MockBehavior.Strict);
            _console.Setup(c => c.Out).Returns(Mock.Of<IStandardStreamWriter>());
            _console.Setup(c => c.Error).Returns(Mock.Of<IStandardStreamWriter>());

            _commandHandler = new IdlToSchemataCommandHandler(
                _console.Object,
                new IdlTokenizer(),
                new IdlCompiler(new DefaultFileProvider())
            );
        }

        [TearDown]
        public void TearDown()
        {
            _tempDir?.Dispose();
        }

        [Test]
        public async Task HandleAsync_GivenValidParameters_WritesExpectedOutput()
        {
            const string input = SimpleTestIdl;

            var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
            File.WriteAllText(sourceFile.FullName, input);

            var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);

            var result = await _commandHandler.HandleCommandAsync(sourceFile, true, sourceDir, CancellationToken.None);
            var resultFileContents = await File.ReadAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "TestRecord.avsc")).ConfigureAwait(false);

            const string expectedResultFileContents = @"{""type"":""record"",""name"":""TestRecord"",""fields"":[{""name"":""FirstName"",""type"":""string""},{""name"":""LastName"",""type"":""string""}]}";

            Assert.That(result, Is.Zero);
            Assert.That(resultFileContents, Is.EqualTo(expectedResultFileContents).Using(LineEndingInvariantStringComparer.Ordinal));
        }

        [Test]
        public async Task HandleAsync_GivenMissingFile_ReturnsError()
        {
            var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
            var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);

            var result = await _commandHandler.HandleCommandAsync(sourceFile, true, sourceDir, CancellationToken.None);

            Assert.That(result, Is.Not.Zero);
        }

        [Test]
        public async Task HandleAsync_GivenInvalidTokens_ReturnsError()
        {
            const string input = "%";

            var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
            File.WriteAllText(sourceFile.FullName, input);

            var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);

            var result = await _commandHandler.HandleCommandAsync(sourceFile, true, sourceDir, CancellationToken.None);

            Assert.That(result, Is.Not.Zero);
        }

        [Test]
        public async Task HandleAsync_GivenValidIdlTokensButInvalidProtocol_ReturnsError()
        {
            const string input = @"record Foo {{
    string label;
}}";

            var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
            File.WriteAllText(sourceFile.FullName, input);

            var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);

            var result = await _commandHandler.HandleCommandAsync(sourceFile, true, sourceDir, CancellationToken.None);

            Assert.That(result, Is.Not.Zero);
        }

        [Test]
        public async Task HandleAsync_GivenOutputAlreadyExistsWithoutOverwrite_ReturnsError()
        {
            const string input = SimpleTestIdl;

            var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
            File.WriteAllText(sourceFile.FullName, input);

            // copy to ensure it already exists
            File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestRecord.avsc"));

            var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);

            var result = await _commandHandler.HandleCommandAsync(sourceFile, false, sourceDir, CancellationToken.None);

            Assert.That(result, Is.Not.Zero);
        }

        [Test]
        public async Task HandleAsync_GivenOutputAlreadyExistsWithOverwrite_Succeeds()
        {
            const string input = SimpleTestIdl;

            var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
            File.WriteAllText(sourceFile.FullName, input);

            // copy to ensure it already exists
            File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestRecord.avsc"));

            var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);

            var result = await _commandHandler.HandleCommandAsync(sourceFile, true, sourceDir, CancellationToken.None);

            Assert.That(result, Is.Zero);
        }

        [Test]
        public async Task HandleAsync_GivenMissingDirectory_ResolvesToSourceFileDir()
        {
            const string input = SimpleTestIdl;

            var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
            File.WriteAllText(sourceFile.FullName, input);

            // copy to ensure it already exists
            File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestRecord.avsc"));

            // expect an error in overwriting if in the same dir
            var result = await _commandHandler.HandleCommandAsync(sourceFile, false, null, CancellationToken.None);

            Assert.That(result, Is.Not.Zero);
        }

        [Test]
        public async Task HandleAsync_GivenErrorInCompilation_ReturnsError()
        {
            var consoleWriter = new Mock<IStandardStreamWriter>();
            consoleWriter
                .Setup(c => c.Write(It.IsAny<string>()))
                .Throws(new Exception("test ex"));

            _console.Setup(c => c.Out).Returns(consoleWriter.Object);

            const string input = SimpleTestIdl;

            var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
            File.WriteAllText(sourceFile.FullName, input);

            var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);

            var result = await _commandHandler.HandleCommandAsync(sourceFile, true, sourceDir, CancellationToken.None);

            Assert.That(result, Is.Not.Zero);
        }
    }
}
