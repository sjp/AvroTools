using Newtonsoft.Json;

namespace SJP.Avro.Tools.Schema.Model;

internal record TimestampMicrosType
{
    [JsonProperty("type")]
    public string Type { get; } = "long";

    [JsonProperty("logicalType")]
    public string LogicalType { get; } = "timestamp-micros";
}
