using Newtonsoft.Json;

namespace SJP.Avro.Tools.Schema.Model;

internal record TimeMicrosType
{
    [JsonProperty("type")]
    public string Type { get; } = "int";

    [JsonProperty("logicalType")]
    public string LogicalType { get; } = "time-micros";
}