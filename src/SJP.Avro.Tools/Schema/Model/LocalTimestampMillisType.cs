using Newtonsoft.Json;

namespace SJP.Avro.Tools.Schema.Model
{
    internal record LocalTimestampMillisType
    {
        [JsonProperty("type")]
        public string Type { get; } = "long";

        [JsonProperty("logicalType")]
        public string LogicalType { get; } = "local-timestamp-millis";
    }
}
