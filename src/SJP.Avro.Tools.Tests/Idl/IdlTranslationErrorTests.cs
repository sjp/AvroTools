using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SJP.Avro.Tools.Idl;

namespace SJP.Avro.Tools.Tests.Idl;

[TestFixture]
internal class IdlTranslationErrorTests
{
    [Test]
    public void Translate_GivenNullStream_ThrowsArgumentNullException()
    {
        var translator = new IdlToAvroTranslator(new PhysicalIdlFileReader());

        Assert.ThrowsAsync<ArgumentNullException>(() => translator.Translate((Stream)null!, TestContext.CurrentContext.CancellationToken));
    }

    [Test]
    public void Translate_GivenASyntaxError_CarriesThePositionItWasFoundAt()
    {
        const string idl = """
                           protocol P {
                             record R { string a; # }
                           }
                           """;
        var translator = new IdlToAvroTranslator(new PhysicalIdlFileReader());

        var thrown = Assert.ThrowsAsync<IdlTranslationException>(() => translator.Translate(idl, TestContext.CurrentContext.CancellationToken));

        Assert.Multiple(() =>
        {
            Assert.That(thrown.FileName, Is.Null);
            Assert.That(thrown.LineNumber, Is.EqualTo(2));
            Assert.That(thrown.ColumnNumber, Is.EqualTo(23));
        });
    }

    [Test]
    public void Translate_GivenADeclarationThatCannotBeTranslated_CarriesThePositionItWasFoundAt()
    {
        const string idl = """
                           protocol P {
                             int f() oneway;
                           }
                           """;
        var translator = new IdlToAvroTranslator(new PhysicalIdlFileReader());

        var thrown = Assert.ThrowsAsync<IdlTranslationException>(() => translator.Translate(idl, TestContext.CurrentContext.CancellationToken));

        Assert.Multiple(() =>
        {
            Assert.That(thrown.LineNumber, Is.EqualTo(2));
            Assert.That(thrown.ColumnNumber, Is.EqualTo(2));
            Assert.That(thrown.Message, Does.Contain("One-way message must return void"));
        });
    }

    [Test]
    public void Translate_GivenASyntaxErrorInAnImportedDocument_NamesTheFileAndThePositionWithinIt()
    {
        var reader = new DictionaryIdlFileReader
        {
            ["inner.avdl"] = """
                             protocol Inner {
                               record R { string a; # }
                             }
                             """
        };
        var translator = new IdlToAvroTranslator(reader);

        var thrown = Assert.ThrowsAsync<IdlTranslationException>(
            () => translator.Translate("protocol Main { import idl \"inner.avdl\"; }", TestContext.CurrentContext.CancellationToken));

        Assert.Multiple(() =>
        {
            Assert.That(thrown.FileName, Is.EqualTo("inner.avdl"));
            Assert.That(thrown.LineNumber, Is.EqualTo(2));
            Assert.That(thrown.ColumnNumber, Is.EqualTo(23));
        });
    }

    [Test]
    public void Translate_GivenAFileReaderThatIsCancelled_DoesNotReportCancellationAsAFailedImport()
    {
        var translator = new IdlToAvroTranslator(new CancellingIdlFileReader());

        Assert.CatchAsync<OperationCanceledException>(
            () => translator.Translate("protocol Main { import idl \"inner.avdl\"; }", TestContext.CurrentContext.CancellationToken));
    }

    [Test]
    public void Translate_GivenACancelledTokenWhileImportingAProtocol_DoesNotReportCancellationAsAFailedImport()
    {
        var reader = new DictionaryIdlFileReader { ["inner.avpr"] = """{ "protocol": "Inner", "types": [], "messages": {} }""" };
        var translator = new IdlToAvroTranslator(reader);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.CatchAsync<OperationCanceledException>(
            () => translator.Translate("protocol Main { import protocol \"inner.avpr\"; }", cancellation.Token));
    }

    [Test]
    public async Task Translate_GivenAnImportFromASubdirectory_ResolvesThatDocumentsOwnImportsAgainstIt()
    {
        var reader = new DictionaryIdlFileReader
        {
            ["sub/inner.avdl"] = "@namespace(\"nested\") protocol Inner { import idl \"deep.avdl\"; record InnerRecord { nested.DeepRecord d; } }",
            ["sub/deep.avdl"] = "@namespace(\"nested\") protocol Deep { record DeepRecord { string y; } }"
        };
        var translator = new IdlToAvroTranslator(reader);

        var result = await translator.Translate(
            "protocol Main { import idl \"sub/inner.avdl\"; record Outer { nested.InnerRecord i; } }",
            TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Json.SelectToken("types[0].name")?.Value<string>(), Is.EqualTo("DeepRecord"));
    }

    [Test]
    public async Task Translate_GivenAnImportThatClimbsOutOfItsDirectory_ReducesThePathBeforeReadingIt()
    {
        var reader = new DictionaryIdlFileReader
        {
            ["sub/inner.avdl"] = "@namespace(\"nested\") protocol Inner { import idl \"../top.avdl\"; record InnerRecord { nested.TopRecord t; } }",
            ["top.avdl"] = "@namespace(\"nested\") protocol Top { record TopRecord { string y; } }"
        };
        var translator = new IdlToAvroTranslator(reader);

        var result = await translator.Translate(
            "protocol Main { import idl \"sub/inner.avdl\"; record Outer { nested.InnerRecord i; } }",
            TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Json.SelectToken("types[0].name")?.Value<string>(), Is.EqualTo("TopRecord"));
    }

    /// <summary>
    /// Serves documents from memory, keyed by the path an import resolves to.
    /// </summary>
    private sealed class DictionaryIdlFileReader : IIdlFileReader
    {
        private readonly Dictionary<string, string> _files = [];

        public string this[string path]
        {
            set => _files[path] = value;
        }

        public Stream OpenRead(string path)
        {
            if (!_files.TryGetValue(path, out var content))
                throw new FileNotFoundException($"File not found: {path}", path);

            return new MemoryStream(Encoding.UTF8.GetBytes(content));
        }
    }

    /// <summary>
    /// Stands in for a reader that observes cancellation of its own accord.
    /// </summary>
    private sealed class CancellingIdlFileReader : IIdlFileReader
    {
        public Stream OpenRead(string path) => throw new OperationCanceledException();
    }
}
