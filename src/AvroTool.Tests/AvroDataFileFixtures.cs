using Avro;
using Avro.File;
using Avro.Generic;

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
}
