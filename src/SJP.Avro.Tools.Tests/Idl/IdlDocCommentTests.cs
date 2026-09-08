using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SJP.Avro.Tools.Idl;

namespace SJP.Avro.Tools.Tests.Idl;

[TestFixture]
internal class IdlDocCommentTests
{
    private const string InputNamespace = "SJP.Avro.Tools.Tests.Idl.Data.Input";

    private static readonly IFileProvider InputFileProvider = new EmbeddedFileProvider(Assembly.GetExecutingAssembly(), InputNamespace);

    private TemporaryDirectory _tempDir;
    private IdlToAvroTranslator _translator;

    [SetUp]
    public void Setup()
    {
        _tempDir = new TemporaryDirectory();
        _translator = new IdlToAvroTranslator(new PhysicalIdlFileReader());
    }

    [TearDown]
    public void TearDown() => _tempDir.Dispose();

    [Test]
    public async Task Translate_GivenADocumentWithoutStrayDocComments_ReportsNoWarnings()
    {
        const string idl = "protocol P { /** documented */ record R { int a; } }";

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        Assert.Multiple(() =>
        {
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(result.Json.SelectToken("types[0].doc")?.Value<string>(), Is.EqualTo("documented"));
        });
    }

    [Test]
    public async Task Translate_GivenADocCommentBeforeAnImport_TranslatesTheDocumentAndWarns()
    {
        _tempDir.WriteFile("inner.avdl", "@namespace(\"nested\") protocol Inner { record InnerRecord { string x; } }");
        var main = _tempDir.WriteFile("main.avdl", "protocol Main {\n  /** stray */\n  import idl \"inner.avdl\";\n  record Outer { nested.InnerRecord i; }\n}");

        var result = await TranslateFile(main);

        Assert.Multiple(() =>
        {
            Assert.That(result.Warnings, Is.EqualTo(new[] { WarningAt(2, 3) }));
            Assert.That(TypeNames(result), Is.EqualTo(new[] { "nested.InnerRecord", "Outer" }));
        });
    }

    [Test]
    public async Task Translate_GivenADocCommentBeforeAClosingBrace_TranslatesTheDocumentAndWarns()
    {
        const string idl = "protocol P {\n  record R {\n    int a;\n    /** stray */\n  }\n}";

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        Assert.Multiple(() =>
        {
            Assert.That(result.Warnings, Is.EqualTo(new[] { WarningAt(4, 5) }));
            Assert.That(result.Json.SelectToken("types[0].fields[0].name")?.Value<string>(), Is.EqualTo("a"));
        });
    }

    [Test]
    public async Task Translate_GivenADocCommentAfterTheLastDeclaration_TranslatesTheDocumentAndWarns()
    {
        const string idl = "protocol P {\n  record R { int a; }\n  /** stray */\n}";

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Warnings, Is.EqualTo(new[] { WarningAt(3, 3) }));
    }

    [Test]
    public async Task Translate_GivenTwoConsecutiveDocComments_TakesTheSecondAndWarnsAboutTheFirst()
    {
        const string idl = "protocol P {\n  /** first */\n  /** second */\n  record R { int a; }\n}";

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        Assert.Multiple(() =>
        {
            Assert.That(result.Json.SelectToken("types[0].doc")?.Value<string>(), Is.EqualTo("second"));
            Assert.That(result.Warnings, Is.EqualTo(new[] { WarningAt(2, 3) }));
        });
    }

    [Test]
    public async Task Translate_GivenAnImportedDocumentWithAStrayDocComment_NamesTheImportInTheWarning()
    {
        var inner = _tempDir.WriteFile("inner.avdl", "@namespace(\"nested\") protocol Inner {\n  record InnerRecord { string x; }\n  /** stray */\n}");
        var main = _tempDir.WriteFile("main.avdl", "protocol Main { import idl \"inner.avdl\"; record Outer { nested.InnerRecord i; } }");

        var result = await TranslateFile(main);

        Assert.That(result.Warnings, Is.EqualTo(new[] { $"{inner}: {WarningAt(3, 3)}" }));
    }

    [Test]
    public async Task Translate_GivenAnIndentedBlockInsideADocComment_KeepsTheIndentationRelativeToTheStars()
    {
        const string idl = """
            protocol P {
              /**
               * Example:
               *     a();
               */
              record R { int a; }
            }
            """;

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Json.SelectToken("types[0].doc")?.Value<string>(), Is.EqualTo("Example:\n    a();"));
    }

    [Test]
    public async Task Translate_GivenBulletsInsideADocComment_KeepsTheAsterisksThatAreContent()
    {
        const string idl = """
            protocol P {
              /**
               * Notes:
               * * first
               * * second
               */
              record R { int a; }
            }
            """;

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Json.SelectToken("types[0].doc")?.Value<string>(), Is.EqualTo("Notes:\n* first\n* second"));
    }

    [Test]
    public async Task Translate_GivenADocCommentWithoutStars_StripsOnlyTheIndentationEveryLineShares()
    {
        const string idl = """
            protocol P {
              /**
                 Summary
                   indented
                 Trailing
               */
              record R { int a; }
            }
            """;

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Json.SelectToken("types[0].doc")?.Value<string>(), Is.EqualTo("Summary\n  indented\nTrailing"));
    }

    [Test]
    public async Task Translate_GivenADocCommentAlignedWithDoubleAsterisks_StripsThemAsThePrefix()
    {
        const string idl = """
            protocol P {
              /**
               ** Summary
               **   indented
               */
              record R { int a; }
            }
            """;

        var result = await _translator.Translate(idl, TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Json.SelectToken("types[0].doc")?.Value<string>(), Is.EqualTo("Summary\n  indented"));
    }

    [Test]
    public async Task Translate_GivenDocCommentsInPositionsNoDeclarationCanClaim_WarnsAboutEachOfThem()
    {
        var file = InputFileProvider.GetFileInfo("comments.avdl");
        await using var stream = file.CreateReadStream();

        var result = await _translator.Translate(stream, TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Warnings, Is.EqualTo(new[]
        {
            WarningAt(4, 8),   // between 'enum' and the enum's name
            WarningAt(4, 45),  // between the enum's name and its body
            WarningAt(5, 5),   // ahead of an enum symbol
            WarningAt(6, 5),
            WarningAt(7, 5),
            WarningAt(8, 5),   // before the enum's closing brace
            WarningAt(8, 29),  // before the enum's default clause
            WarningAt(8, 55),  // before the terminating semicolon
            WarningAt(13, 9),  // between 'fixed' and the fixed type's name
            WarningAt(13, 48), // before the fixed type's size
            WarningAt(24, 5),  // before a record's closing brace
            WarningAt(31, 3),  // superseded by the comment on the line below it
            WarningAt(38, 12), // between 'throws' and the error it names
            WarningAt(41, 3)   // after the last declaration in the protocol
        }));
    }

    private Task<IdlParseResult> TranslateFile(string filePath)
    {
        var content = File.ReadAllText(filePath);
        return _translator.Translate(content, Path.GetDirectoryName(filePath), filePath, TestContext.CurrentContext.CancellationToken);
    }

    private static string[] TypeNames(IdlParseResult result)
    {
        return result.Json.SelectTokens("types[*]")
            .Select(t => t["namespace"] != null ? $"{t["namespace"]}.{t["name"]}" : t["name"]!.ToString())
            .ToArray();
    }

    private static string WarningAt(int line, int column)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "Line {0}, char {1}: Ignoring out-of-place documentation comment. Did you mean to use a multiline comment ( /* ... */ ) instead?",
            line,
            column);
    }
}
