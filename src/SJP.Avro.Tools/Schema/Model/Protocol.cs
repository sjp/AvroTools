using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SJP.Avro.Tools.Schema.Model;

internal record Protocol
{
    [JsonProperty("protocol")]
    public string Name { get; set; } = default!;

    [JsonProperty("namespace", NullValueHandling = NullValueHandling.Ignore)]
    public string? Namespace { get; set; }

    [JsonProperty("doc", NullValueHandling = NullValueHandling.Ignore)]
    public string? Documentation { get; set; }

    [JsonProperty("types", NullValueHandling = NullValueHandling.Ignore)]
    public IEnumerable<JObject>? Types { get; set; }

    [JsonProperty("messages", NullValueHandling = NullValueHandling.Ignore)]
    public IReadOnlyDictionary<string, JObject>? Messages { get; set; }
}
