using System.IO;
using System.Text;
using Avro;
using Avro.File;
using Avro.Generic;
using Avro.IO;

namespace AvroTool.Tests;

/// <summary>
/// Builds Avro object container files for command tests, since fixtures for the data-file
/// commands are binary rather than the plain-text schema/IDL fixtures used elsewhere.
/// </summary>
internal static class AvroDataFileFixtures
{
    public static void WriteContainerFile(string path, RecordSchema schema, params GenericRecord[] records)
    {
        using var writer = DataFileWriter<GenericRecord>.OpenWriter(new GenericDatumWriter<GenericRecord>(schema), path, Codec.CreateCodec(Codec.Type.Null));
        foreach (var record in records)
            writer.Append(record);

        writer.Flush();
    }

    /// <summary>
    /// Writes a container's header only, declaring the given codec name. No data blocks follow,
    /// which is enough to exercise codec resolution without needing a working implementation of
    /// the codec itself.
    /// </summary>
    public static void WriteContainerHeaderWithCodec(string path, RecordSchema schema, string codecName)
    {
        using var stream = File.Create(path);
        stream.Write("Obj"u8);
        stream.WriteByte(1);

        var encoder = new BinaryEncoder(stream);
        encoder.WriteMapStart();
        encoder.SetItemCount(2);
        encoder.StartItem();
        encoder.WriteString("avro.schema");
        encoder.WriteBytes(Encoding.UTF8.GetBytes(schema.ToString()));
        encoder.StartItem();
        encoder.WriteString("avro.codec");
        encoder.WriteBytes(Encoding.UTF8.GetBytes(codecName));
        encoder.WriteMapEnd();

        stream.Write(new byte[16]);
    }
}
