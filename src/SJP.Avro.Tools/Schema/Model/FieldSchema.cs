using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SJP.Avro.Tools.Schema.Model;

internal record FieldSchema
{
    /// <summary>
    /// List of aliases for the field name.
    /// </summary>
    [JsonProperty("aliases", NullValueHandling = NullValueHandling.Ignore)]
    public IEnumerable<string>? Aliases { get; set; }

    /// <summary>
    /// Name of the field.
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; set; } = default!;

    /// <summary>
    /// Position of the field within its record.
    /// </summary>
    [JsonIgnore]
    public int Position { get; set; }

    /// <summary>
    /// Documentation for the field, if any. Null if there is no documentation.
    /// </summary>
    [JsonProperty("doc", NullValueHandling = NullValueHandling.Ignore)]
    public string? Documentation { get; set; }

    /// <summary>
    /// The default value for the field stored as JSON object, if defined. Otherwise, null.
    /// </summary>
    [JsonProperty("default", NullValueHandling = NullValueHandling.Ignore)]
    public JToken? DefaultValue { get; set; }

    /// <summary>
    /// Order of the field
    /// </summary>
    [JsonProperty("order", NullValueHandling = NullValueHandling.Ignore)]
    public string? Ordering { get; set; }

    /// <summary>
    /// Type of the field.
    /// </summary>
    [JsonProperty("type")]
    public JToken Type { get; init; } = default!;
}
