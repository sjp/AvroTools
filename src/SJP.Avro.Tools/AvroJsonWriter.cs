using System;
using System.IO;
using Avro;
using Avro.Generic;

namespace SJP.Avro.Tools;

/// <summary>
/// Encodes Avro datums (e.g. <see cref="GenericRecord"/>) as JSON following Avro's JSON
/// encoding rules.
/// </summary>
public static class AvroJsonWriter
{
    /// <summary>
    /// Encodes a datum for the given schema as a single-line JSON string.
    /// </summary>
    /// <param name="schema">The schema the datum conforms to.</param>
    /// <param name="datum">The datum to encode, e.g. a <see cref="GenericRecord"/>. <c>null</c> is a
    /// valid datum for a <c>"null"</c> schema, or for a union with a <c>"null"</c> branch.</param>
    /// <returns>The datum encoded as JSON, following Avro's JSON encoding conventions.</returns>
    /// <remarks>
    /// Everything this builds to encode the datum comes from the schema, and is thrown away
    /// afterwards. Encoding many datums of one schema costs far less through an
    /// <see cref="AvroJsonEncoder"/>, which is built once and writes each datum to a
    /// <see cref="TextWriter"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="schema"/> is <c>null</c>.</exception>
    /// <exception cref="AvroTypeException">The datum does not match the schema, e.g. a value of the
    /// wrong type for a field, or a value matching no branch of a union.</exception>
    /// <exception cref="AvroException">The datum could not be encoded against the schema, e.g. a
    /// record missing a field the schema declares.</exception>
    public static string Encode(Schema schema, object? datum)
    {
        ArgumentNullException.ThrowIfNull(schema);

        using var stringWriter = new StringWriter();

        var encoder = new AvroJsonEncoder(schema, stringWriter);
        encoder.Write(datum);

        return stringWriter.ToString();
    }
}
