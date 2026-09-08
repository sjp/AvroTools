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
    /// Cache of named schemas for reference resolution.
    /// Maps fully qualified names to their JSON schema definitions.
    /// </summary>
    public Dictionary<string, JObject> NamedSchemas { get; } = [];

    /// <summary>
    /// Set of import paths that have been processed to prevent circular imports.
    /// </summary>
    public HashSet<string> ProcessedImports { get; } = [];

    /// <summary>
    /// Set of schema names that have been processed and added to a known set of types.
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

    /// <summary>
    /// The namespace of the named type currently being translated, if any. Bare type references
    /// are resolved against this before falling back to <see cref="DefaultNamespace"/>, mirroring
    /// how the enclosing named type's namespace is inherited while its fields are processed.
    /// </summary>
    public string? CurrentNamespace { get; set; }

    /// <summary>
    /// The directory that relative import paths are resolved against, i.e. the directory holding
    /// the document being parsed. When <c>null</c>, import paths are used exactly as written.
    /// </summary>
    public string? BaseDirectory { get; set; }
}
