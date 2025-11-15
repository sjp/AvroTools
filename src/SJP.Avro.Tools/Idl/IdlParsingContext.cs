using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// Represents the parsing context for IDL to Avro translation.
/// This class encapsulates all mutable state during the parsing process.
/// </summary>
public sealed record IdlParsingContext
{
    /// <summary>
    /// The file path being parsed.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Cache of named schemas for reference resolution.
    /// Maps fully qualified names to their JSON schema definitions.
    /// </summary>
    public Dictionary<string, JObject> NamedSchemas { get; } = [];

    /// <summary>
    /// Set of import paths that have been processed to prevent circular imports.
    /// </summary>
    public HashSet<string> ProcessedImports { get; } = [];

    /// <summary>
    /// Set of schema names that have been processed and added to the types array.
    /// </summary>
    public HashSet<string> ProcessedSchemas { get; } = [];

    /// <summary>
    /// Set of schema names that have been inlined as forward references.
    /// </summary>
    public HashSet<string> InlinedForwardRefs { get; } = [];

    /// <summary>
    /// Indicates whether forward references should be tracked during parsing.
    /// </summary>
    public bool TrackForwardReferences { get; set; }

    /// <summary>
    /// The default namespace for the current parsing context.
    /// </summary>
    public string? DefaultNamespace { get; set; }
}
