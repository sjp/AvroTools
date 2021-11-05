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
using SJP.Avro.Tools.CodeGen;
using SJP.Avro.Tools.Idl;

namespace SJP.Avro.AvroTool.Tests.Handlers
{
    [TestFixture]
    internal class CodeGenCommandHandlerTests
    {
        private const string TestNamespace = "SJP.Arvo.CodeGen.Test";

        private const string SimpleTestIdl = @"protocol TestProtocol {
  record TestRecord {
    string FirstName;
    string LastName;
  }
}
";

        private const string SimpleTestIdlWithMessages = @"protocol TestProtocol {
  void `error`();
  void `void`();
}";

        private const string SimpleTestProtocol = @"{""protocol"":""TestProtocol"",""types"":[],""messages"":{""error"":{""request"":[],""response"":""null""},""void"":{""request"":[],""response"":""null""}}}";

        private const string SimpleTestSchema = @"{""type"":""record"",""name"":""TestRecord"",""fields"":[{""name"":""FirstName"",""type"":""string""},{""name"":""LastName"",""type"":""string""}]}";

        private TemporaryDirectory _tempDir;
        private Mock<IConsole> _console;
        private CodeGenCommandHandler _commandHandler;

        [SetUp]
        public void Setup()
        {
            _tempDir = new TemporaryDirectory();

            _console = new Mock<IConsole>(MockBehavior.Strict);
            _console.Setup(c => c.Out).Returns(Mock.Of<IStandardStreamWriter>());
            _console.Setup(c => c.Error).Returns(Mock.Of<IStandardStreamWriter>());

            _commandHandler = new CodeGenCommandHandler(
                _console.Object,
                new IdlTokenizer(),
                new IdlCompiler(new DefaultFileProvider()),
                new CodeGeneratorResolver()
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

            var result = await _commandHandler.HandleCommandAsync(sourceFile, true, TestNamespace, sourceDir, CancellationToken.None).ConfigureAwait(false);
            var resultFileContents = await File.ReadAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "TestRecord.cs")).ConfigureAwait(false);

            const string expectedResultFileContents = @"using System;
using System.Collections.Generic;
using Avro;
using Avro.Specific;

namespace SJP.Arvo.CodeGen.Test
{
    public record TestRecord : ISpecificRecord
    {
        private static readonly Schema _schema = Schema.Parse(""{\""type\"":\""record\"",\""name\"":\""TestRecord\"",\""fields\"":[{\""name\"":\""FirstName\"",\""type\"":\""string\""},{\""name\"":\""LastName\"",\""type\"":\""string\""}]}"");

        public Schema Schema { get; } = _schema;

        public string FirstName { get; set; } = default!;

        public string LastName { get; set; } = default!;

        public object Get(int fieldPos)
        {
            var testRecordField = (TestRecordField)fieldPos;
            return testRecordField switch
            {
                TestRecordField.FirstName => FirstName,
                TestRecordField.LastName => LastName,
                _ => throw new AvroRuntimeException(""Bad index "" + fieldPos + "" in Get()"")
            };
        }

        public void Put(int fieldPos, object fieldValue)
        {
            var testRecordField = (TestRecordField)fieldPos;
            switch (testRecordField)
            {
                case TestRecordField.FirstName:
                    FirstName = (string)fieldValue;
                    break;
                case TestRecordField.LastName:
                    LastName = (string)fieldValue;
                    break;
                default:
                    throw new AvroRuntimeException(""Bad index "" + fieldPos + "" in Put()"");
            }
        }

        private enum TestRecordField
        {
            FirstName,
            LastName
        }
    }
}";

            Assert.That(result, Is.Zero);
            Assert.That(resultFileContents, Is.EqualTo(expectedResultFileContents).Using(LineEndingInvariantStringComparer.Ordinal));
        }

        [Test]
        public async Task HandleAsync_GivenValidParametersForIdlWithProtocol_WritesExpectedOutput()
        {
            const string input = SimpleTestIdlWithMessages;

            var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
            File.WriteAllText(sourceFile.FullName, input);

            var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);

            var result = await _commandHandler.HandleCommandAsync(sourceFile, true, TestNamespace, sourceDir, CancellationToken.None).ConfigureAwait(false);
            var resultFileContents = await File.ReadAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "TestProtocol.cs")).ConfigureAwait(false);

            const string expectedResultFileContents = @"using System;
using System.Collections.Generic;
using Avro;
using Avro.IO;
using Avro.Specific;

namespace SJP.Arvo.CodeGen.Test
{
    public abstract record TestProtocol : ISpecificProtocol
    {
        private static readonly Protocol _protocol = Protocol.Parse(""{\""protocol\"":\""TestProtocol\"",\""types\"":[],\""messages\"":{\""error\"":{\""request\"":[],\""response\"":\""null\""},\""void\"":{\""request\"":[],\""response\"":\""null\""}}}"");

        public Protocol Protocol { get; } = _protocol;

        public void Request(ICallbackRequestor requestor, string messageName, object[] args, object callback)
        {
            switch (messageName)
            {
                case ""error"":
                    requestor.Request<object>(messageName, args, callback);
                    break;
                case ""void"":
                    requestor.Request<object>(messageName, args, callback);
                    break;
            }
        }

        public abstract void error();

        public abstract void void();
    }
}";

            Assert.That(result, Is.Zero);
            Assert.That(resultFileContents, Is.EqualTo(expectedResultFileContents).Using(LineEndingInvariantStringComparer.Ordinal));
        }

        [Test]
        public async Task HandleAsync_GivenValidParametersForProtocolInput_WritesExpectedOutput()
        {
            const string input = SimpleTestProtocol;

            var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avpr"));
            File.WriteAllText(sourceFile.FullName, input);

            var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);

            var result = await _commandHandler.HandleCommandAsync(sourceFile, true, TestNamespace, sourceDir, CancellationToken.None).ConfigureAwait(false);
            var resultFileContents = await File.ReadAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "TestProtocol.cs")).ConfigureAwait(false);

            const string expectedResultFileContents = @"using System;
using System.Collections.Generic;
using Avro;
using Avro.IO;
using Avro.Specific;

namespace SJP.Arvo.CodeGen.Test
{
    public abstract record TestProtocol : ISpecificProtocol
    {
        private static readonly Protocol _protocol = Protocol.Parse(""{\""protocol\"":\""TestProtocol\"",\""types\"":[],\""messages\"":{\""error\"":{\""request\"":[],\""response\"":\""null\""},\""void\"":{\""request\"":[],\""response\"":\""null\""}}}"");

        public Protocol Protocol { get; } = _protocol;

        public void Request(ICallbackRequestor requestor, string messageName, object[] args, object callback)
        {
            switch (messageName)
            {
                case ""error"":
                    requestor.Request<object>(messageName, args, callback);
                    break;
                case ""void"":
                    requestor.Request<object>(messageName, args, callback);
                    break;
            }
        }

        public abstract void error();

        public abstract void void();
    }
}";

            Assert.That(result, Is.Zero);
            Assert.That(resultFileContents, Is.EqualTo(expectedResultFileContents).Using(LineEndingInvariantStringComparer.Ordinal));
        }

        [Test]
        public async Task HandleAsync_GivenValidParametersForSchemaInput_WritesExpectedOutput()
        {
            const string input = SimpleTestSchema;

            var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avsc"));
            File.WriteAllText(sourceFile.FullName, input);

            var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);

            var result = await _commandHandler.HandleCommandAsync(sourceFile, true, TestNamespace, sourceDir, CancellationToken.None).ConfigureAwait(false);
            var resultFileContents = await File.ReadAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "TestRecord.cs")).ConfigureAwait(false);

            const string expectedResultFileContents = @"using System;
using System.Collections.Generic;
using Avro;
using Avro.Specific;

namespace SJP.Arvo.CodeGen.Test
{
    public record TestRecord : ISpecificRecord
    {
        private static readonly Schema _schema = Schema.Parse(""{\""type\"":\""record\"",\""name\"":\""TestRecord\"",\""fields\"":[{\""name\"":\""FirstName\"",\""type\"":\""string\""},{\""name\"":\""LastName\"",\""type\"":\""string\""}]}"");

        public Schema Schema { get; } = _schema;

        public string FirstName { get; set; } = default!;

        public string LastName { get; set; } = default!;

        public object Get(int fieldPos)
        {
            var testRecordField = (TestRecordField)fieldPos;
            return testRecordField switch
            {
                TestRecordField.FirstName => FirstName,
                TestRecordField.LastName => LastName,
                _ => throw new AvroRuntimeException(""Bad index "" + fieldPos + "" in Get()"")
            };
        }

        public void Put(int fieldPos, object fieldValue)
        {
            var testRecordField = (TestRecordField)fieldPos;
            switch (testRecordField)
            {
                case TestRecordField.FirstName:
                    FirstName = (string)fieldValue;
                    break;
                case TestRecordField.LastName:
                    LastName = (string)fieldValue;
                    break;
                default:
                    throw new AvroRuntimeException(""Bad index "" + fieldPos + "" in Put()"");
            }
        }

        private enum TestRecordField
        {
            FirstName,
            LastName
        }
    }
}";

            Assert.That(result, Is.Zero);
            Assert.That(resultFileContents, Is.EqualTo(expectedResultFileContents).Using(LineEndingInvariantStringComparer.Ordinal));
        }

        [Test]
        public async Task HandleAsync_GivenMissingFile_ReturnsError()
        {
            var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
            var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);

            var result = await _commandHandler.HandleCommandAsync(sourceFile, true, TestNamespace, sourceDir, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Is.Not.Zero);
        }

        [Test]
        public async Task HandleAsync_GivenInvalidTokens_ReturnsError()
        {
            const string input = "%";

            var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
            File.WriteAllText(sourceFile.FullName, input);

            var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);

            var result = await _commandHandler.HandleCommandAsync(sourceFile, true, TestNamespace, sourceDir, CancellationToken.None).ConfigureAwait(false);

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

            var result = await _commandHandler.HandleCommandAsync(sourceFile, true, TestNamespace, sourceDir, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Is.Not.Zero);
        }

        [Test]
        public async Task HandleAsync_GivenOutputAlreadyExistsWithoutOverwrite_ReturnsError()
        {
            const string input = SimpleTestIdl;

            var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
            File.WriteAllText(sourceFile.FullName, input);

            // copy to ensure it already exists
            File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestRecord.cs"));

            var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);

            var result = await _commandHandler.HandleCommandAsync(sourceFile, false, TestNamespace, sourceDir, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Is.Not.Zero);
        }

        [Test]
        public async Task HandleAsync_GivenOutputAlreadyExistsWithoutOverwriteForProtocol_ReturnsError()
        {
            const string input = SimpleTestProtocol;

            var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avpr"));
            File.WriteAllText(sourceFile.FullName, input);

            // copy to ensure it already exists
            File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestProtocol.cs"));

            var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);

            var result = await _commandHandler.HandleCommandAsync(sourceFile, false, TestNamespace, sourceDir, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Is.Not.Zero);
        }

        [Test]
        public async Task HandleAsync_GivenOutputAlreadyExistsWithOverwrite_Succeeds()
        {
            const string input = SimpleTestIdl;

            var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
            File.WriteAllText(sourceFile.FullName, input);

            // copy to ensure it already exists
            File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestRecord.cs"));

            var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);

            var result = await _commandHandler.HandleCommandAsync(sourceFile, true, TestNamespace, sourceDir, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Is.Zero);
        }

        [Test]
        public async Task HandleAsync_GivenOutputAlreadyExistsWithOverwriteForProtocol_Succeeds()
        {
            const string input = SimpleTestProtocol;

            var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avpr"));
            File.WriteAllText(sourceFile.FullName, input);

            // copy to ensure it already exists
            File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestProtocol.cs"));

            var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);

            var result = await _commandHandler.HandleCommandAsync(sourceFile, true, TestNamespace, sourceDir, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Is.Zero);
        }

        [Test]
        public async Task HandleAsync_GivenMissingDirectory_ResolvesToCurrentDir()
        {
            const string input = SimpleTestIdl;

            var originalDir = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(_tempDir.DirectoryPath);

            var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
            File.WriteAllText(sourceFile.FullName, input);

            // copy to ensure it already exists
            File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestRecord.cs"));

            // expect an error in overwriting if in the same dir
            var result = await _commandHandler.HandleCommandAsync(sourceFile, false, TestNamespace, null, CancellationToken.None).ConfigureAwait(false);

            // restore dir
            Directory.SetCurrentDirectory(originalDir);

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

            var result = await _commandHandler.HandleCommandAsync(sourceFile, true, TestNamespace, sourceDir, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Is.Not.Zero);
        }
    }
}
