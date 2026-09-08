using System;
using System.IO;
using Avro;
using Avro.Generic;
using Avro.IO;
using Newtonsoft.Json;

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
    /// <exception cref="ArgumentNullException"><paramref name="schema"/> is <c>null</c>.</exception>
    /// <exception cref="AvroTypeException">The datum does not match the schema, e.g. a value of the
    /// wrong type for a field, or a value matching no branch of a union.</exception>
    /// <exception cref="AvroException">The datum could not be encoded against the schema, e.g. a
    /// record missing a field the schema declares.</exception>
    public static string Encode(Schema schema, object? datum)
    {
        ArgumentNullException.ThrowIfNull(schema);

        using var stringWriter = new StringWriter();
        using (var jsonWriter = new JsonTextWriter(stringWriter))
        {
            var encoder = new JsonEncoder(schema, jsonWriter)
            {
                // Apache.Avro's JsonEncoder defaults this to false, which drops the union
                // type-wrapper entirely for named-type branches (record/enum/fixed) instead of
                // using their name -- e.g. a ["null", Inner] union encodes as the bare inner
                // value rather than {"ns.Inner": {...}}, which isn't valid Avro JSON. Setting
                // this to true is what actually produces spec-compliant output.
                IncludeNamespace = true
            };

            var writer = new GenericDatumWriter<object>(schema);
            // Apache.Avro's writer declares its datum non-nullable, but null is the datum a
            // "null" schema (or the null branch of a union) is written from.
            writer.Write(datum!, encoder);
            encoder.Flush();
        }

        return stringWriter.ToString();
    }
}
