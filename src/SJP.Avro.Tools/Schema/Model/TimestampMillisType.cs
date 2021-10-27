using Newtonsoft.Json;

namespace SJP.Avro.Tools.Schema.Model
{
    public class TimestampMillisType
    {
        [JsonProperty("type")]
        public string Type { get; } = "long";

        [JsonProperty("logicalType")]
        public string LogicalType { get; } = "timestamp-millis";
    }
}
