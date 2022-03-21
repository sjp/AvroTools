using Newtonsoft.Json;

namespace SJP.Avro.Tools.Schema.Model;

internal record DecimalType
{
    public DecimalType(int precision, int scale)
    {
        Precision = precision;
        Scale = scale;
    }

    [JsonProperty("type")]
    public string Type { get; } = "bytes";

    [JsonProperty("logicalType")]
    public string LogicalType { get; } = "decimal";

    [JsonProperty("precision")]
    public int Precision { get; }

    [JsonProperty("scale")]
    public int Scale { get; }
}
