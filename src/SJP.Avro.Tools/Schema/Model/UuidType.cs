using Newtonsoft.Json;

namespace SJP.Avro.Tools.Schema.Model
{
    public class UuidType
    {
        [JsonProperty("type")]
        public string Type { get; } = "string";

        [JsonProperty("logicalType")]
        public string LogicalType { get; } = "uuid";
    }
}
