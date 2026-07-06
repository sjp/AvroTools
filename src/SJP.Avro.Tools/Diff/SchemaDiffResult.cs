using System.Collections.Generic;
using System.Linq;

namespace SJP.Avro.Tools.Diff;

/// <summary>
/// The outcome of comparing a "before" schema against an "after" schema.
/// </summary>
public sealed class SchemaDiffResult
{
    /// <summary>
    /// Initialises a new instance of the <see cref="SchemaDiffResult"/> class.
    /// </summary>
    /// <param name="changes">The changes detected; an empty collection means the schemas are semantically identical.</param>
    public SchemaDiffResult(IEnumerable<SchemaChange> changes)
    {
        Changes = changes?.ToList() ?? [];
    }

    /// <summary>The changes detected between the "before" and "after" schemas, in the order they were found.</summary>
    public IReadOnlyList<SchemaChange> Changes { get; }

    /// <summary><c>true</c> when no changes were detected, i.e. the schemas are semantically identical.</summary>
    public bool IsIdentical => Changes.Count == 0;
}
