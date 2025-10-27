﻿using System.Collections.Generic;
using Newtonsoft.Json;

namespace SJP.Avro.Tools.Schema.Model;

internal record Error : NamedSchema
{
    [JsonProperty("type")]
    public string Type { get; } = "error";

    [JsonProperty("fields")]
    public IEnumerable<FieldSchema> Fields { get; set; } = [];
}