using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SJP.Avro.Tools.Schema.Model
{
    public record Record : NamedSchema
    {
        [JsonProperty("type")]
        public string Type { get; } = "record";

        [JsonProperty("fields")]
        public IEnumerable<FieldSchema> Fields { get; set; } = Array.Empty<FieldSchema>();
    }
}
