using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AvroTool.Commands;
using Moq;
using NUnit.Framework;
using SJP.Avro.Tools.CodeGen;
using SJP.Avro.Tools.Idl;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;
using AvroProtocol = Avro.Protocol;
using AvroSchema = Avro.Schema;

namespace AvroTool.Tests.Commands;

[TestFixture]
internal class CodeGenCommandTests
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
    private Mock<IAnsiConsole> _console;
    private Mock<IIdlToAvroTranslator> _idlTranslator;
    private CommandContext _commandContext;
    private CodeGenCommand _commandHandler;

    private IdlParseResult _parseResult;

    [SetUp]
    public void Setup()
    {
        _tempDir = new TemporaryDirectory();

        _console = new Mock<IAnsiConsole>(MockBehavior.Strict);
        _console.Setup(c => c.Write(It.IsAny<IRenderable>()));

        _parseResult = IdlParseResult.Schema(AvroSchema.Parse(SimpleTestSchema));
        _idlTranslator = new Mock<IIdlToAvroTranslator>(MockBehavior.Strict);
        _idlTranslator
            .Setup(t => t.Translate(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _parseResult);

        _commandContext = new CommandContext([], Mock.Of<IRemainingArguments>(), "codegen", null);

        _commandHandler = new CodeGenCommand(
            _console.Object,
            new CodeGeneratorResolver(),
            _idlTranslator.Object
        );
    }

    [TearDown]
    public void TearDown()
    {
        _tempDir?.Dispose();
    }

    [Test]
    public async Task ExecuteAsync_GivenValidParameters_WritesExpectedOutput()
    {
        const string input = SimpleTestIdl;

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, input);

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var command = new CodeGenCommand.Settings
        {
            InputFile = sourceFile.FullName,
            Overwrite = true,
            Namespace = TestNamespace,
            OutputDirectory = sourceDir,
        };
        var result = await _commandHandler.ExecuteAsync(_commandContext, command, default);
        var resultFileContents = await File.ReadAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "TestRecord.cs"));

        const string expectedResultFileContents = """
using System;
using System.Collections.Generic;
using Avro;
using Avro.Specific;

namespace SJP.Arvo.CodeGen.Test
{
    public record TestRecord : ISpecificRecord
    {
        private static readonly Schema _schema = Schema.Parse("{\"type\":\"record\",\"name\":\"TestRecord\",\"fields\":[{\"name\":\"FirstName\",\"type\":\"string\"},{\"name\":\"LastName\",\"type\":\"string\"}]}");

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
                _ => throw new AvroRuntimeException("Bad index " + fieldPos + " in Get()")
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
                    throw new AvroRuntimeException("Bad index " + fieldPos + " in Put()");
            }
        }

        private enum TestRecordField
        {
            FirstName,
            LastName
        }
    }
}
""";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(resultFileContents, Is.EqualTo(expectedResultFileContents).IgnoreLineEndingFormat);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenValidParametersForIdlWithProtocol_WritesExpectedOutput()
    {
        const string input = SimpleTestIdlWithMessages;
        _parseResult = IdlParseResult.Protocol(AvroProtocol.Parse(SimpleTestProtocol));

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, input);

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var command = new CodeGenCommand.Settings
        {
            InputFile = sourceFile.FullName,
            Overwrite = true,
            Namespace = TestNamespace,
            OutputDirectory = sourceDir,
        };
        var result = await _commandHandler.ExecuteAsync(_commandContext, command, default);
        var resultFileContents = await File.ReadAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "TestProtocol.cs"));

        const string expectedResultFileContents = """
using System;
using System.Collections.Generic;
using Avro;
using Avro.IO;
using Avro.Specific;

namespace SJP.Arvo.CodeGen.Test
{
    public abstract record TestProtocol : ISpecificProtocol
    {
        private static readonly Protocol _protocol = Protocol.Parse("{\"protocol\":\"TestProtocol\",\"types\":[],\"messages\":{\"error\":{\"request\":[],\"response\":\"null\"},\"void\":{\"request\":[],\"response\":\"null\"}}}");

        public Protocol Protocol { get; } = _protocol;

        public void Request(ICallbackRequestor requestor, string messageName, object[] args, object callback)
        {
            switch (messageName)
            {
                case "error":
                    requestor.Request<object>(messageName, args, callback);
                    break;
                case "void":
                    requestor.Request<object>(messageName, args, callback);
                    break;
            }
        }

        public abstract void error();

        public abstract void void();
    }
}
""";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(resultFileContents, Is.EqualTo(expectedResultFileContents).IgnoreLineEndingFormat);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenValidParametersForProtocolInput_WritesExpectedOutput()
    {
        const string input = SimpleTestProtocol;

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avpr"));
        await File.WriteAllTextAsync(sourceFile.FullName, input);

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var command = new CodeGenCommand.Settings
        {
            InputFile = sourceFile.FullName,
            Overwrite = true,
            Namespace = TestNamespace,
            OutputDirectory = sourceDir,
        };
        var result = await _commandHandler.ExecuteAsync(_commandContext, command, default);
        var resultFileContents = await File.ReadAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "TestProtocol.cs"));

        const string expectedResultFileContents = """
using System;
using System.Collections.Generic;
using Avro;
using Avro.IO;
using Avro.Specific;

namespace SJP.Arvo.CodeGen.Test
{
    public abstract record TestProtocol : ISpecificProtocol
    {
        private static readonly Protocol _protocol = Protocol.Parse("{\"protocol\":\"TestProtocol\",\"types\":[],\"messages\":{\"error\":{\"request\":[],\"response\":\"null\"},\"void\":{\"request\":[],\"response\":\"null\"}}}");

        public Protocol Protocol { get; } = _protocol;

        public void Request(ICallbackRequestor requestor, string messageName, object[] args, object callback)
        {
            switch (messageName)
            {
                case "error":
                    requestor.Request<object>(messageName, args, callback);
                    break;
                case "void":
                    requestor.Request<object>(messageName, args, callback);
                    break;
            }
        }

        public abstract void error();

        public abstract void void();
    }
}
""";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(resultFileContents, Is.EqualTo(expectedResultFileContents).IgnoreLineEndingFormat);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenValidParametersForSchemaInput_WritesExpectedOutput()
    {
        const string input = SimpleTestSchema;

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avsc"));
        await File.WriteAllTextAsync(sourceFile.FullName, input);

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var command = new CodeGenCommand.Settings
        {
            InputFile = sourceFile.FullName,
            Overwrite = true,
            Namespace = TestNamespace,
            OutputDirectory = sourceDir,
        };
        var result = await _commandHandler.ExecuteAsync(_commandContext, command, default);
        var resultFileContents = await File.ReadAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "TestRecord.cs"));

        const string expectedResultFileContents = """
using System;
using System.Collections.Generic;
using Avro;
using Avro.Specific;

namespace SJP.Arvo.CodeGen.Test
{
    public record TestRecord : ISpecificRecord
    {
        private static readonly Schema _schema = Schema.Parse("{\"type\":\"record\",\"name\":\"TestRecord\",\"fields\":[{\"name\":\"FirstName\",\"type\":\"string\"},{\"name\":\"LastName\",\"type\":\"string\"}]}");

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
                _ => throw new AvroRuntimeException("Bad index " + fieldPos + " in Get()")
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
                    throw new AvroRuntimeException("Bad index " + fieldPos + " in Put()");
            }
        }

        private enum TestRecordField
        {
            FirstName,
            LastName
        }
    }
}
""";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(resultFileContents, Is.EqualTo(expectedResultFileContents).IgnoreLineEndingFormat);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenInvalidInput_ReturnsError()
    {
        const string input = "%";

        _idlTranslator
            .Setup(t => t.Translate(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("something went wrong"));

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, input);

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var command = new CodeGenCommand.Settings
        {
            InputFile = sourceFile.FullName,
            Overwrite = true,
            Namespace = TestNamespace,
            OutputDirectory = sourceDir,
        };
        var result = await _commandHandler.ExecuteAsync(_commandContext, command, default);

        Assert.That(result, Is.Not.Zero);
    }

    [Test]
    public async Task ExecuteAsync_GivenOutputAlreadyExistsWithoutOverwrite_ReturnsError()
    {
        const string input = SimpleTestIdl;

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, input);

        // copy to ensure it already exists
        File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestRecord.cs"));

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var command = new CodeGenCommand.Settings
        {
            InputFile = sourceFile.FullName,
            Overwrite = false,
            Namespace = TestNamespace,
            OutputDirectory = sourceDir,
        };
        var result = await _commandHandler.ExecuteAsync(_commandContext, command, default);

        Assert.That(result, Is.Not.Zero);
    }

    [Test]
    public async Task ExecuteAsync_GivenOutputAlreadyExistsWithoutOverwriteForProtocol_ReturnsError()
    {
        const string input = SimpleTestProtocol;

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avpr"));
        await File.WriteAllTextAsync(sourceFile.FullName, input);

        // copy to ensure it already exists
        File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestProtocol.cs"));

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var command = new CodeGenCommand.Settings
        {
            InputFile = sourceFile.FullName,
            Overwrite = false,
            Namespace = TestNamespace,
            OutputDirectory = sourceDir,
        };
        var result = await _commandHandler.ExecuteAsync(_commandContext, command, default);

        Assert.That(result, Is.Not.Zero);
    }

    [Test]
    public async Task ExecuteAsync_GivenOutputAlreadyExistsWithOverwrite_Succeeds()
    {
        const string input = SimpleTestIdl;

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, input);

        // copy to ensure it already exists
        File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestRecord.cs"));

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var command = new CodeGenCommand.Settings
        {
            InputFile = sourceFile.FullName,
            Overwrite = true,
            Namespace = TestNamespace,
            OutputDirectory = sourceDir,
        };
        var result = await _commandHandler.ExecuteAsync(_commandContext, command, default);

        Assert.That(result, Is.Zero);
    }

    [Test]
    public async Task ExecuteAsync_GivenOutputAlreadyExistsWithOverwriteForProtocol_Succeeds()
    {
        const string input = SimpleTestProtocol;

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avpr"));
        await File.WriteAllTextAsync(sourceFile.FullName, input);

        // copy to ensure it already exists
        File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestProtocol.cs"));

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var command = new CodeGenCommand.Settings
        {
            InputFile = sourceFile.FullName,
            Overwrite = true,
            Namespace = TestNamespace,
            OutputDirectory = sourceDir,
        };
        var result = await _commandHandler.ExecuteAsync(_commandContext, command, default);

        Assert.That(result, Is.Zero);
    }

    [Test]
    public async Task ExecuteAsync_GivenMissingDirectory_ResolvesToCurrentDir()
    {
        const string input = SimpleTestIdl;

        var originalDir = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_tempDir.DirectoryPath);

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, input);

        // copy to ensure it already exists
        File.Copy(sourceFile.FullName, Path.Combine(_tempDir.DirectoryPath, "TestRecord.cs"));

        // expect an error in overwriting if in the same dir
        var command = new CodeGenCommand.Settings
        {
            InputFile = sourceFile.FullName,
            Overwrite = false,
            Namespace = TestNamespace,
            OutputDirectory = null,
        };
        var result = await _commandHandler.ExecuteAsync(_commandContext, command, default);

        // restore dir
        Directory.SetCurrentDirectory(originalDir);

        Assert.That(result, Is.Not.Zero);
    }

    [Test]
    public void Validate_WithMissingInputFile_ReturnsError()
    {
        var settings = new CodeGenCommand.Settings
        {
            InputFile = string.Empty,
            Namespace = TestNamespace
        };

        var result = _commandHandler.Validate(_commandContext, settings);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Successful, Is.False);
            Assert.That(result.Message, Is.EqualTo("An input file must be provided."));
        }
    }

    [Test]
    public void Validate_WithNonExistentInputFile_ReturnsError()
    {
        var settings = new CodeGenCommand.Settings
        {
            InputFile = "a/b/c.avdl",
            Namespace = TestNamespace
        };

        var result = _commandHandler.Validate(_commandContext, settings);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Successful, Is.False);
            Assert.That(result.Message, Is.EqualTo($"An input file could not be found at: {settings.InputFile}"));
        }
    }

    [Test]
    public void Validate_WithInvalidNamespace_ReturnsError()
    {
        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        File.WriteAllText(sourceFile.FullName, SimpleTestIdl);

        var settings = new CodeGenCommand.Settings
        {
            InputFile = sourceFile.FullName,
            Namespace = "123"
        };

        var result = _commandHandler.Validate(_commandContext, settings);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Successful, Is.False);
            Assert.That(result.Message, Is.EqualTo($"The value '{settings.Namespace}' is not a valid C# namespace."));
        }
    }

    [Test]
    public void Validate_WithValidParameters_ReturnsSuccess()
    {
        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        File.WriteAllText(sourceFile.FullName, SimpleTestIdl);

        var settings = new CodeGenCommand.Settings
        {
            InputFile = sourceFile.FullName,
            Namespace = TestNamespace
        };

        var result = _commandHandler.Validate(_commandContext, settings);
        Assert.That(result.Successful, Is.True);
    }
}