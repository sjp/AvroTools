using System;
using System.IO;
using System.Text;
using Avro;
using Avro.Generic;
using Avro.IO;
using Newtonsoft.Json;

namespace SJP.Avro.Tools;

/// <summary>
/// Encodes a sequence of Avro datums (e.g. <see cref="GenericRecord"/>) of one schema as JSON,
/// following Avro's JSON encoding rules.
/// </summary>
/// <remarks>
/// <para>
/// Everything Apache.Avro builds to encode a datum — the tree of write delegates behind
/// <see cref="GenericDatumWriter{T}"/>, and the grammar and parser behind
/// <see cref="JsonEncoder"/> — is derived from the schema rather than from the datum, and none
/// of it is cached between calls. Encoding many datums of one schema through
/// <see cref="AvroJsonWriter.Encode"/> therefore analyses that schema once per datum, which for
/// an object container file of any size costs far more than encoding the data does. An instance
/// of this class does that analysis once and writes every datum through it.
/// </para>
/// <para>
/// The encoder is stateful and is not thread-safe: use one instance per thread. A failed
/// <see cref="Write"/> can leave the encoder's parser part way through a datum, so an instance
/// should be discarded rather than reused after one throws.
/// </para>
/// </remarks>
public sealed class AvroJsonEncoder
{
    private readonly GenericDatumWriter<object> _writer;
    private readonly JsonEncoder _encoder;

    /// <summary>
    /// Creates an encoder writing datums of the given schema to the given writer.
    /// </summary>
    /// <param name="schema">The schema every datum written here conforms to.</param>
    /// <param name="output">The writer each datum's JSON is written to. It is not owned by the
    /// encoder: it is never flushed, closed or disposed, so the caller decides when written text
    /// reaches its destination.</param>
    /// <param name="indent">Whether each datum is written over several lines, indented by two
    /// spaces per level, rather than on one line. Indenting is done as the JSON is written, so it
    /// costs a fraction of what re-serializing the compact text does, and changes nothing but
    /// whitespace.</param>
    /// <exception cref="ArgumentNullException"><paramref name="schema"/> or <paramref name="output"/>
    /// is <c>null</c>.</exception>
    public AvroJsonEncoder(Schema schema, TextWriter output, bool indent = false)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(output);

        Schema = schema;

        var jsonWriter = new JsonTextWriter(new UnflushableTextWriter(output))
        {
            // The writer outlives no datum in particular, so it must never take the destination
            // down with it, or close out a datum that a failed write left half-written.
            CloseOutput = false,
            AutoCompleteOnClose = false,

            // Nothing separates one datum from the next, indented or not: Newtonsoft's writer
            // only indents within a datum, so consecutive datums still run together and the
            // caller decides what goes between them.
            Formatting = indent ? Formatting.Indented : Formatting.None,
            Indentation = 2
        };

        _encoder = new JsonEncoder(schema, jsonWriter)
        {
            // Apache.Avro's JsonEncoder defaults this to false, which drops the union
            // type-wrapper entirely for named-type branches (record/enum/fixed) instead of
            // using their name -- e.g. a ["null", Inner] union encodes as the bare inner
            // value rather than {"ns.Inner": {...}}, which isn't valid Avro JSON. Setting
            // this to true is what actually produces spec-compliant output.
            IncludeNamespace = true
        };

        _writer = new GenericDatumWriter<object>(schema);
    }

    /// <summary>
    /// The schema every datum written through this encoder conforms to.
    /// </summary>
    public Schema Schema { get; }

    /// <summary>
    /// Encodes a datum and writes it to the encoder's writer, with no separator before or after
    /// it. Consecutive datums are written one straight after another, so a caller wanting them
    /// on separate lines writes the line ending itself.
    /// </summary>
    /// <param name="datum">The datum to encode, e.g. a <see cref="GenericRecord"/>. <c>null</c> is a
    /// valid datum for a <c>"null"</c> schema, or for a union with a <c>"null"</c> branch.</param>
    /// <exception cref="AvroTypeException">The datum does not match the schema, e.g. a value of the
    /// wrong type for a field, or a value matching no branch of a union.</exception>
    /// <exception cref="AvroException">The datum could not be encoded against the schema, e.g. a
    /// record missing a field the schema declares.</exception>
    public void Write(object? datum)
    {
        // Apache.Avro's writer declares its datum non-nullable, but null is the datum a
        // "null" schema (or the null branch of a union) is written from.
        _writer.Write(datum!, _encoder);

        // Avro's encoder defers the actions that close a datum out -- the braces ending a record,
        // for one -- until it is flushed, so a datum is only complete once this returns.
        _encoder.Flush();
    }

    /// <summary>
    /// A writer that passes text through but swallows flushes.
    /// </summary>
    /// <remarks>
    /// Each datum ends in a flush of Avro's encoder (see <see cref="AvroJsonEncoder.Write(object?)"/>), and Newtonsoft's
    /// writer passes that flush on to the destination. The destination is typically buffered
    /// precisely so that many datums travel together, and flushing it per datum would undo that;
    /// nothing is lost by ignoring the flush, because the JSON writer holds no text of its own
    /// between tokens.
    /// </remarks>
    private sealed class UnflushableTextWriter(TextWriter inner) : TextWriter
    {
        /// <inheritdoc />
        public override Encoding Encoding => inner.Encoding;

        /// <inheritdoc />
        public override IFormatProvider FormatProvider => inner.FormatProvider;

        /// <inheritdoc />
        public override void Write(char value) => inner.Write(value);

        /// <inheritdoc />
        public override void Write(string? value) => inner.Write(value);

        /// <inheritdoc />
        public override void Write(char[] buffer, int index, int count) => inner.Write(buffer, index, count);

        /// <inheritdoc />
        public override void Write(ReadOnlySpan<char> buffer) => inner.Write(buffer);

        /// <inheritdoc />
        public override void Flush()
        {
        }
    }
}
