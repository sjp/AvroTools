using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SJP.Avro.Tools.Schema.Model;

internal record EnumType : NamedSchema
{
    [JsonProperty("type")]
    public string Type { get; } = "enum";

    [JsonProperty("symbols")]
    public IEnumerable<string> Symbols { get; set; } = [];

    [JsonProperty("default", NullValueHandling = NullValueHandling.Ignore)]
    public string? DefaultValue { get; set; }
}