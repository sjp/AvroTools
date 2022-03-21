using Newtonsoft.Json;

namespace SJP.Avro.Tools.Schema.Model;

internal record DurationType
{
    [JsonProperty("type")]
    public string Type { get; } = "fixed";

    [JsonProperty("logicalType")]
    public string LogicalType { get; } = "duration";

    [JsonProperty("size")]
    public int Size { get; } = 12;
}
