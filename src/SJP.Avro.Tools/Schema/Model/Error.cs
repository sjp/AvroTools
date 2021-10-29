using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SJP.Avro.Tools.Schema.Model
{
    public record Error : NamedSchema
    {
        [JsonProperty("type")]
        public string Type { get; } = "error";

        [JsonProperty("fields")]
        public IEnumerable<FieldSchema> Fields { get; set; } = Array.Empty<FieldSchema>();
    }
}
