﻿using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SJP.Avro.Tools.Schema.Model;

internal record Message
{
    [JsonProperty("doc", NullValueHandling = NullValueHandling.Ignore)]
    public string? Documentation { get; set; }

    [JsonProperty("request")]
    public IEnumerable<MessageParameter> Request { get; set; } = [];

    [JsonProperty("response")]
    public JToken? Response { get; set; } = JToken.FromObject("null");

    [JsonProperty("one-way", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool OneWay { get; set; }

    [JsonProperty("errors", NullValueHandling = NullValueHandling.Ignore)]
    public IEnumerable<string>? Errors { get; set; }
}