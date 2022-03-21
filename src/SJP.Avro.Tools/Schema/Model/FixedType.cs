using Newtonsoft.Json;

namespace SJP.Avro.Tools.Schema.Model;

internal record FixedType : NamedSchema
{
    [JsonProperty("type")]
    public string Type { get; } = "fixed";

    [JsonProperty("size")]
    public int Size { get; set; }
}
