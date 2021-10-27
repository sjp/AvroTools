using Newtonsoft.Json;

namespace SJP.Avro.Tools.Schema.Model
{
    public class DateType
    {
        [JsonProperty("type")]
        public string Type { get; } = "int";

        [JsonProperty("logicalType")]
        public string LogicalType { get; } = "date";
    }
}
