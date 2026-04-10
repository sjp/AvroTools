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
using Spectre.Console.Cli.Testing;
using Spectre.Console.Rendering;
using AvroProtocol = Avro.Protocol;
using AvroSchema = Avro.Schema;

namespace AvroTool.Tests.Commands;

[TestFixture]
internal class CodeGenCommandTests
{
    private const string TestNamespace = "SJP.Avro.CodeGen.Test";

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

    private const string MultiRecordSchema = """
{
  "type": "record",
  "name": "TestRecord",
  "namespace": "TestNamespace",
  "fields": [
    {
      "name": "data",
      "type": {
        "type": "array",
        "items": {
          "type": "record",
          "name": "Datum",
          "namespace": "TestNamespace",
          "fields": [
            {
              "name": "name",
              "type": [
                "null",
                "string"
              ]
            },
            {
              "name": "datumId",
              "type": "int"
            },
            {
              "name": "pairVolumes",
              "type": {
                "type": "record",
                "name": "PairVolume",
                "namespace": "TestNamespace",
                "fields": [
                  {
                    "name": "negative1",
                    "type": [
                      "null",
                      "double"
                    ]
                  },
                  {
                    "name": "negative2",
                    "type": [
                      "null",
                      "double"
                    ]
                  }
                ]
              }
            }
          ]
        }
      }
    }
  ]
}
""";

    private CommandAppTester _app;
    private TemporaryDirectory _tempDir;
    private Mock<IAnsiConsole> _console;
    private Mock<IIdlToAvroTranslator> _idlTranslator;

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

        var registrar = new FakeTypeRegistrar();
        var command = new CodeGenCommand(_console.Object, new CodeGeneratorResolver(), _idlTranslator.Object);
        registrar.RegisterInstance(typeof(CodeGenCommand), command);

        _app = new CommandAppTester(registrar);
        _app.SetDefaultCommand<CodeGenCommand>();
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
        var result = await _app.RunAsync([sourceFile.FullName, TestNamespace, "--overwrite", "--output-dir", sourceDir.FullName], default);
        var resultFileContents = await File.ReadAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "TestRecord.cs"));

        const string expectedResultFileContents = """
using System;
using System.Collections.Generic;
using Avro;
using Avro.Specific;
using AvroSchema = Avro.Schema;

namespace SJP.Avro.CodeGen.Test
{
    public record TestRecord : ISpecificRecord
    {
        private static readonly AvroSchema _schema = AvroSchema.Parse("{\"type\":\"record\",\"name\":\"TestRecord\",\"fields\":[{\"name\":\"FirstName\",\"type\":\"string\"},{\"name\":\"LastName\",\"type\":\"string\"}]}");

        public AvroSchema Schema { get; } = _schema;

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
            Assert.That(result.ExitCode, Is.Zero);
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
        var result = await _app.RunAsync([sourceFile.FullName, TestNamespace, "--overwrite", "--output-dir", sourceDir.FullName], default);
        var resultFileContents = await File.ReadAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "TestProtocol.cs"));

        const string expectedResultFileContents = """
using System;
using System.Collections.Generic;
using Avro;
using Avro.IO;
using Avro.Specific;
using AvroProtocol = Avro.Protocol;

namespace SJP.Avro.CodeGen.Test
{
    public abstract record TestProtocol : ISpecificProtocol
    {
        private static readonly AvroProtocol _protocol = AvroProtocol.Parse("{\"protocol\":\"TestProtocol\",\"types\":[],\"messages\":{\"error\":{\"request\":[],\"response\":\"null\"},\"void\":{\"request\":[],\"response\":\"null\"}}}");

        public AvroProtocol Protocol { get; } = _protocol;

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
            Assert.That(result.ExitCode, Is.Zero);
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
        var result = await _app.RunAsync([sourceFile.FullName, TestNamespace, "--overwrite", "--output-dir", sourceDir.FullName], default);
        var resultFileContents = await File.ReadAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "TestProtocol.cs"));

        const string expectedResultFileContents = """
using System;
using System.Collections.Generic;
using Avro;
using Avro.IO;
using Avro.Specific;
using AvroProtocol = Avro.Protocol;

namespace SJP.Avro.CodeGen.Test
{
    public abstract record TestProtocol : ISpecificProtocol
    {
        private static readonly AvroProtocol _protocol = AvroProtocol.Parse("{\"protocol\":\"TestProtocol\",\"types\":[],\"messages\":{\"error\":{\"request\":[],\"response\":\"null\"},\"void\":{\"request\":[],\"response\":\"null\"}}}");

        public AvroProtocol Protocol { get; } = _protocol;

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
            Assert.That(result.ExitCode, Is.Zero);
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
        var result = await _app.RunAsync([sourceFile.FullName, TestNamespace, "--overwrite", "--output-dir", sourceDir.FullName], default);
        var resultFileContents = await File.ReadAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "TestRecord.cs"));

        const string expectedResultFileContents = """
using System;
using System.Collections.Generic;
using Avro;
using Avro.Specific;
using AvroSchema = Avro.Schema;

namespace SJP.Avro.CodeGen.Test
{
    public record TestRecord : ISpecificRecord
    {
        private static readonly AvroSchema _schema = AvroSchema.Parse("{\"type\":\"record\",\"name\":\"TestRecord\",\"fields\":[{\"name\":\"FirstName\",\"type\":\"string\"},{\"name\":\"LastName\",\"type\":\"string\"}]}");

        public AvroSchema Schema { get; } = _schema;

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
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(resultFileContents, Is.EqualTo(expectedResultFileContents).IgnoreLineEndingFormat);
        }
    }

    [Test]
    public async Task ExecuteAsync_GivenValidSchemaInputWithMultipleNamedTypes_WritesExpectedOutput()
    {
        const string input = MultiRecordSchema;

        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avsc"));
        await File.WriteAllTextAsync(sourceFile.FullName, input);

        var sourceDir = new DirectoryInfo(_tempDir.DirectoryPath);
        var result = await _app.RunAsync([sourceFile.FullName, TestNamespace, "--overwrite", "--output-dir", sourceDir.FullName], default);
        var pairVolumeFileContents = await File.ReadAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "TestNamespace.PairVolume.cs"));
        var datumFileContents = await File.ReadAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "TestNamespace.Datum.cs"));
        var testRecordFileContents = await File.ReadAllTextAsync(Path.Combine(_tempDir.DirectoryPath, "TestNamespace.TestRecord.cs"));

        const string ExpectedPairVolumeFileContents = """
using System;
using System.Collections.Generic;
using Avro;
using Avro.Specific;
using AvroSchema = Avro.Schema;

namespace TestNamespace
{
    public record PairVolume : ISpecificRecord
    {
        private static readonly AvroSchema _schema = AvroSchema.Parse("{\"type\":\"record\",\"name\":\"PairVolume\",\"namespace\":\"TestNamespace\",\"fields\":[{\"name\":\"negative1\",\"type\":[\"null\",\"double\"]},{\"name\":\"negative2\",\"type\":[\"null\",\"double\"]}]}");

        public AvroSchema Schema { get; } = _schema;

        public double? negative1 { get; set; }

        public double? negative2 { get; set; }

        public object Get(int fieldPos)
        {
            var pairVolumeField = (PairVolumeField)fieldPos;
            return pairVolumeField switch
            {
                PairVolumeField.negative1 => negative1,
                PairVolumeField.negative2 => negative2,
                _ => throw new AvroRuntimeException("Bad index " + fieldPos + " in Get()")
            };
        }

        public void Put(int fieldPos, object fieldValue)
        {
            var pairVolumeField = (PairVolumeField)fieldPos;
            switch (pairVolumeField)
            {
                case PairVolumeField.negative1:
                    negative1 = (double?)fieldValue;
                    break;
                case PairVolumeField.negative2:
                    negative2 = (double?)fieldValue;
                    break;
                default:
                    throw new AvroRuntimeException("Bad index " + fieldPos + " in Put()");
            }
        }

        private enum PairVolumeField
        {
            negative1,
            negative2
        }
    }
}
""";

        const string ExpectedDatumFileContents = """
using System;
using System.Collections.Generic;
using Avro;
using Avro.Specific;
using AvroSchema = Avro.Schema;

namespace TestNamespace
{
    public record Datum : ISpecificRecord
    {
        private static readonly AvroSchema _schema = AvroSchema.Parse("{\"type\":\"record\",\"name\":\"Datum\",\"namespace\":\"TestNamespace\",\"fields\":[{\"name\":\"name\",\"type\":[\"null\",\"string\"]},{\"name\":\"datumId\",\"type\":\"int\"},{\"name\":\"pairVolumes\",\"type\":{\"type\":\"record\",\"name\":\"PairVolume\",\"namespace\":\"TestNamespace\",\"fields\":[{\"name\":\"negative1\",\"type\":[\"null\",\"double\"]},{\"name\":\"negative2\",\"type\":[\"null\",\"double\"]}]}}]}");

        public AvroSchema Schema { get; } = _schema;

        public string? name { get; set; }

        public int datumId { get; set; }

        public PairVolume pairVolumes { get; set; } = default!;

        public object Get(int fieldPos)
        {
            var datumField = (DatumField)fieldPos;
            return datumField switch
            {
                DatumField.name => name,
                DatumField.datumId => datumId,
                DatumField.pairVolumes => pairVolumes,
                _ => throw new AvroRuntimeException("Bad index " + fieldPos + " in Get()")
            };
        }

        public void Put(int fieldPos, object fieldValue)
        {
            var datumField = (DatumField)fieldPos;
            switch (datumField)
            {
                case DatumField.name:
                    name = (string?)fieldValue;
                    break;
                case DatumField.datumId:
                    datumId = (int)fieldValue;
                    break;
                case DatumField.pairVolumes:
                    pairVolumes = (PairVolume)fieldValue;
                    break;
                default:
                    throw new AvroRuntimeException("Bad index " + fieldPos + " in Put()");
            }
        }

        private enum DatumField
        {
            name,
            datumId,
            pairVolumes
        }
    }
}
""";

        const string ExpectedTestRecordFileContents = """
using System;
using System.Collections.Generic;
using Avro;
using Avro.Specific;
using AvroSchema = Avro.Schema;

namespace TestNamespace
{
    public record TestRecord : ISpecificRecord
    {
        private static readonly AvroSchema _schema = AvroSchema.Parse("{\"type\":\"record\",\"name\":\"TestRecord\",\"namespace\":\"TestNamespace\",\"fields\":[{\"name\":\"data\",\"type\":{\"type\":\"array\",\"items\":{\"type\":\"record\",\"name\":\"Datum\",\"namespace\":\"TestNamespace\",\"fields\":[{\"name\":\"name\",\"type\":[\"null\",\"string\"]},{\"name\":\"datumId\",\"type\":\"int\"},{\"name\":\"pairVolumes\",\"type\":{\"type\":\"record\",\"name\":\"PairVolume\",\"namespace\":\"TestNamespace\",\"fields\":[{\"name\":\"negative1\",\"type\":[\"null\",\"double\"]},{\"name\":\"negative2\",\"type\":[\"null\",\"double\"]}]}}]}}}]}");

        public AvroSchema Schema { get; } = _schema;

        public List<Datum> data { get; set; } = default!;

        public object Get(int fieldPos)
        {
            var testRecordField = (TestRecordField)fieldPos;
            return testRecordField switch
            {
                TestRecordField.data => data,
                _ => throw new AvroRuntimeException("Bad index " + fieldPos + " in Get()")
            };
        }

        public void Put(int fieldPos, object fieldValue)
        {
            var testRecordField = (TestRecordField)fieldPos;
            switch (testRecordField)
            {
                case TestRecordField.data:
                    data = (List<Datum>)fieldValue;
                    break;
                default:
                    throw new AvroRuntimeException("Bad index " + fieldPos + " in Put()");
            }
        }

        private enum TestRecordField
        {
            data
        }
    }
}
""";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(pairVolumeFileContents, Is.EqualTo(ExpectedPairVolumeFileContents).IgnoreLineEndingFormat);
            Assert.That(datumFileContents, Is.EqualTo(ExpectedDatumFileContents).IgnoreLineEndingFormat);
            Assert.That(testRecordFileContents, Is.EqualTo(ExpectedTestRecordFileContents).IgnoreLineEndingFormat);
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
        var result = await _app.RunAsync([sourceFile.FullName, TestNamespace, "--overwrite", "--output-dir", sourceDir.FullName], default);

        Assert.That(result.ExitCode, Is.Not.Zero);
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
        var result = await _app.RunAsync([sourceFile.FullName, TestNamespace, "--output-dir", sourceDir.FullName], default);

        Assert.That(result.ExitCode, Is.Not.Zero);
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
        var result = await _app.RunAsync([sourceFile.FullName, TestNamespace, "--output-dir", sourceDir.FullName], default);

        Assert.That(result.ExitCode, Is.Not.Zero);
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
        var result = await _app.RunAsync([sourceFile.FullName, TestNamespace, "--overwrite", "--output-dir", sourceDir.FullName], default);

        Assert.That(result.ExitCode, Is.Zero);
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
        var result = await _app.RunAsync([sourceFile.FullName, TestNamespace, "--overwrite", "--output-dir", sourceDir.FullName], default);

        Assert.That(result.ExitCode, Is.Zero);
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
        var result = await _app.RunAsync([sourceFile.FullName, TestNamespace], default);

        // restore dir
        Directory.SetCurrentDirectory(originalDir);

        Assert.That(result.ExitCode, Is.Not.Zero);
    }

    [Test]
    public async Task Validate_WithMissingInputFile_ReturnsError()
    {
        var result = await _app.RunAsync(["", TestNamespace], default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain("An input file must be provided."));
        }
    }

    [Test]
    public async Task Validate_WithNonExistentInputFile_ReturnsError()
    {
        const string InputFile = "a/b/c.avdl";

        var result = await _app.RunAsync([InputFile, TestNamespace], default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain($"An input file could not be found at: {InputFile}"));
        }
    }

    [Test]
    public async Task Validate_WithInvalidNamespace_ReturnsError()
    {
        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, SimpleTestIdl);

        const string CodeNamespace = "123";

        var result = await _app.RunAsync([sourceFile.FullName, CodeNamespace], default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain($"The value '{CodeNamespace}' is not a valid C# namespace."));
        }
    }

    [Test]
    public async Task Validate_WithValidParameters_ReturnsSuccess()
    {
        var sourceFile = new FileInfo(Path.Combine(_tempDir.DirectoryPath, "test_input.avdl"));
        await File.WriteAllTextAsync(sourceFile.FullName, SimpleTestIdl);

        var result = await _app.RunAsync([sourceFile.FullName, TestNamespace], default);

        Assert.That(result.Output, Is.Empty);
    }
}