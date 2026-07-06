using System.Collections.Generic;
using System.Linq;

namespace SJP.Avro.Tools.Compatibility;

/// <summary>
/// The outcome of checking whether data written with a writer schema can be read with a reader schema.
/// </summary>
public sealed class SchemaCompatibilityResult
{
    /// <summary>
    /// Initialises a new instance of the <see cref="SchemaCompatibilityResult"/> class.
    /// </summary>
    /// <param name="incompatibilities">The incompatibilities detected; an empty collection means the schemas are compatible.</param>
    public SchemaCompatibilityResult(IEnumerable<Incompatibility> incompatibilities)
    {
        Incompatibilities = incompatibilities?.ToList() ?? [];
    }

    /// <summary>The incompatibilities detected between the reader and writer, in the order they were found.</summary>
    public IReadOnlyList<Incompatibility> Incompatibilities { get; }

    /// <summary><c>true</c> when no incompatibilities were detected, i.e. the writer's data can be read by the reader.</summary>
    public bool IsCompatible => Incompatibilities.Count == 0;
}
