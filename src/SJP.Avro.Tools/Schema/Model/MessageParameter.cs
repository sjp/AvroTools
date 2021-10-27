﻿using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SJP.Avro.Tools.Schema.Model
{
    public class MessageParameter
    {
        [JsonProperty("name")]
        public string Name { get; set; } = default!;

        [JsonProperty("type")]
        public JToken Type { get; set; } = default!;

        [JsonProperty("default", NullValueHandling = NullValueHandling.Ignore)]
        public IEnumerable<JToken>? Default { get; set; }
    }
}