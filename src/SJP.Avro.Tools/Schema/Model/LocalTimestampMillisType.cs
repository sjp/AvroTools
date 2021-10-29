using Newtonsoft.Json;

namespace SJP.Avro.Tools.Schema.Model
{
    public record LocalTimestampMillisType
    {
        [JsonProperty("type")]
        public string Type { get; } = "long";

        [JsonProperty("logicalType")]
        public string LogicalType { get; } = "local-timestamp-millis";
    }
}
