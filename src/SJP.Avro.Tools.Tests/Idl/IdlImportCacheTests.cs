using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SJP.Avro.Tools.Idl;

namespace SJP.Avro.Tools.Tests.Idl;

[TestFixture]
internal class IdlImportCacheTests
{
    private const string CommonIdl = """
                                     @namespace("shared")
                                     protocol Common {
                                       /** Where someone lives. */
                                       record Address { string street; }
                                     }
                                     """;

    private const string FirstIdl = """protocol First { import idl "common.avdl"; record One { shared.Address a; } }""";

    private const string SecondIdl = """protocol Second { import idl "common.avdl"; record Two { shared.Address a; } }""";

    [Test]
    public async Task Translate_GivenTwoDocumentsImportingTheSameFileThroughOneCache_ReadsTheImportOnce()
    {
        var reader = new CountingIdlFileReader { ["common.avdl"] = CommonIdl };
        var translator = new IdlToAvroTranslator(reader, new IdlImportCache());

        await translator.Translate(FirstIdl, TestContext.CurrentContext.CancellationToken);
        await translator.Translate(SecondIdl, TestContext.CurrentContext.CancellationToken);

        Assert.That(reader.OpenCount("common.avdl"), Is.EqualTo(1));
    }

    [Test]
    public async Task Translate_GivenTwoDocumentsImportingTheSameFileWithoutACache_ReadsTheImportEachTime()
    {
        var reader = new CountingIdlFileReader { ["common.avdl"] = CommonIdl };
        var translator = new IdlToAvroTranslator(reader);

        await translator.Translate(FirstIdl, TestContext.CurrentContext.CancellationToken);
        await translator.Translate(SecondIdl, TestContext.CurrentContext.CancellationToken);

        Assert.That(reader.OpenCount("common.avdl"), Is.EqualTo(2));
    }

    [Test]
    public async Task Translate_GivenAnImportTakenFromTheCache_TranslatesItAsThoughItHadBeenRead()
    {
        var reader = new CountingIdlFileReader { ["common.avdl"] = CommonIdl };
        var uncached = new IdlToAvroTranslator(reader);
        var cached = new IdlToAvroTranslator(reader, new IdlImportCache());

        var expected = await uncached.Translate(SecondIdl, TestContext.CurrentContext.CancellationToken);
        await cached.Translate(FirstIdl, TestContext.CurrentContext.CancellationToken);
        var reused = await cached.Translate(SecondIdl, TestContext.CurrentContext.CancellationToken);

        Assert.That(reused.Json.ToString(), Is.EqualTo(expected.Json.ToString()));
    }

    [Test]
    public async Task Translate_GivenAnImportTakenFromTheCache_CarriesItsDocumentationAsThoughItHadBeenRead()
    {
        var reader = new CountingIdlFileReader { ["common.avdl"] = CommonIdl };
        var translator = new IdlToAvroTranslator(reader, new IdlImportCache());

        await translator.Translate(FirstIdl, TestContext.CurrentContext.CancellationToken);
        var reused = await translator.Translate(SecondIdl, TestContext.CurrentContext.CancellationToken);

        Assert.That(reused.Json.SelectToken("types[0].doc")?.Value<string>(), Is.EqualTo("Where someone lives."));
    }

    [Test]
    public async Task Translate_GivenAnImportTakenFromTheCache_ReportsItsWarningsAgainForTheImportingDocument()
    {
        const string strayComment = """
                                    protocol Common {
                                      record Address { string street; }
                                      /** Nothing follows this. */
                                    }
                                    """;
        var reader = new CountingIdlFileReader { ["common.avdl"] = strayComment };
        var translator = new IdlToAvroTranslator(reader, new IdlImportCache());

        var first = await translator.Translate(FirstIdl.Replace("shared.Address", "Address"), TestContext.CurrentContext.CancellationToken);
        var second = await translator.Translate(SecondIdl.Replace("shared.Address", "Address"), TestContext.CurrentContext.CancellationToken);

        Assert.Multiple(() =>
        {
            Assert.That(first.Warnings, Has.Exactly(1).Contains("Ignoring out-of-place documentation comment"));
            Assert.That(second.Warnings, Is.EqualTo(first.Warnings));
        });
    }

    /// <summary>
    /// Serves documents from memory, counting how often each one is opened.
    /// </summary>
    private sealed class CountingIdlFileReader : IIdlFileReader
    {
        private readonly Dictionary<string, string> _files = [];
        private readonly Dictionary<string, int> _opens = [];

        public string this[string path]
        {
            set => _files[path] = value;
        }

        public int OpenCount(string path) => _opens.TryGetValue(path, out var count) ? count : 0;

        public Stream OpenRead(string path)
        {
            if (!_files.TryGetValue(path, out var content))
                throw new FileNotFoundException($"File not found: {path}", path);

            _opens[path] = OpenCount(path) + 1;

            return new MemoryStream(Encoding.UTF8.GetBytes(content));
        }
    }
}
