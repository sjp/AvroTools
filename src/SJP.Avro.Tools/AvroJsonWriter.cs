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
    /// <param name="datum">The datum to encode, e.g. a <see cref="GenericRecord"/>.</param>
    /// <returns>The datum encoded as JSON, following Avro's JSON encoding conventions.</returns>
    public static string Encode(Schema schema, object datum)
    {
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
            writer.Write(datum, encoder);
            encoder.Flush();
        }

        return stringWriter.ToString();
    }
}
