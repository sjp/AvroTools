using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SJP.Avro.Tools.Schema
{
    public class Field
    {
        /// <summary>
        /// List of aliases for the field name.
        /// </summary>
        public IEnumerable<string> Aliases { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Name of the field.
        /// </summary>
        public string Name { get; set; } = default!;

        /// <summary>
        /// Position of the field within its record.
        /// </summary>
        public int Position { get; set; }

        /// <summary>
        /// Documentation for the field, if any. Null if there is no documentation.
        /// </summary>
        public string? Documentation { get; set; }

        /// <summary>
        /// The default value for the field stored as JSON object, if defined. Otherwise, null.
        /// </summary>
        public JToken? DefaultValue { get; set; }

        /// <summary>
        /// Order of the field
        /// </summary>
        public FieldSortOrder? Ordering { get; set; }
    }

    public enum FieldSortOrder
    {
        /// <summary>
        /// Unknown order.
        /// </summary>
        Unknown,

        /// <summary>
        /// Ascending order.
        /// </summary>
        Ascending,

        /// <summary>
        /// Descending order.
        /// </summary>
        Descending,

        /// <summary>
        /// Ignore sort order.
        /// </summary>
        Ignore
    }

    public class ArrayDto
    {
        [JsonProperty("name")]
        public string Name { get; set; } = default!;

        [JsonProperty("type")]
        public string Type { get; set; } = default!;
    }

    public class MapDto
    {
        [JsonProperty("name")]
        public string Name { get; set; } = default!;

        [JsonProperty("name")]
        public string Type { get; set; } = default!;
    }

    public class UnionDto
    {
        public IEnumerable<JToken> Type { get; set; } = Array.Empty<JToken>();
    }


    public class EnumDto
    {
        [JsonProperty("symbols")]
        public IEnumerable<string> Symbols { get; set; } = Array.Empty<string>();

        [JsonProperty("default", NullValueHandling = NullValueHandling.Ignore)]
        public string? Default { get; set; }
    }


    public class FixedDto
    {
        [JsonProperty("name")]
        public string Name { get; set; } = default!;

        [JsonProperty("size")]
        public int Size { get; set; }
    }

    public abstract class NamedSchemaDto
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
}
