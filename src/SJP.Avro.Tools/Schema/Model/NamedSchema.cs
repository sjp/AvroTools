using System.Collections.Generic;
using Newtonsoft.Json;

namespace SJP.Avro.Tools.Schema.Model;

internal abstract record NamedSchema
{
    [JsonProperty("name")]
    public string Name { get; set; } = default!;

    [JsonProperty("namespace", NullValueHandling = NullValueHandling.Ignore)]
    public string? Namespace { get; set; }

    [JsonProperty("aliases", NullValueHandling = NullValueHandling.Ignore)]
    public IEnumerable<string>? Aliases { get; set; }

    [JsonProperty("doc", NullValueHandling = NullValueHandling.Ignore)]
    public string? Documentation { get; set; }
}