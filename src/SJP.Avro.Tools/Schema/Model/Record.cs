﻿using System.Collections.Generic;
using Newtonsoft.Json;

namespace SJP.Avro.Tools.Schema.Model;

internal record Record : NamedSchema
{
    [JsonProperty("type")]
    public string Type { get; } = "record";

    [JsonProperty("fields")]
    public IEnumerable<FieldSchema> Fields { get; set; } = [];
}