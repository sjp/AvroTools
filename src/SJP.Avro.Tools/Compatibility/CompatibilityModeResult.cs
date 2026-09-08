using System.Collections.Generic;
using System.Linq;

namespace SJP.Avro.Tools.Compatibility;

/// <summary>
/// The outcome of checking a set of schemas under a compatibility mode: every reader/writer
/// comparison the mode calls for, and whether all of them passed.
/// </summary>
public sealed class CompatibilityModeResult
{
    /// <summary>
    /// Initialises a new instance of the <see cref="CompatibilityModeResult"/> class.
    /// </summary>
    /// <param name="mode">The mode the schemas were checked under.</param>
    /// <param name="checks">The comparisons that were made, in the order they were made.</param>
    public CompatibilityModeResult(CompatibilityMode mode, IEnumerable<CompatibilityCheck> checks)
    {
        Mode = mode;
        Checks = checks?.ToList() ?? [];
    }

    /// <summary>The mode the schemas were checked under.</summary>
    public CompatibilityMode Mode { get; }

    /// <summary>The comparisons that were made, in the order they were made.</summary>
    public IReadOnlyList<CompatibilityCheck> Checks { get; }

    /// <summary><c>true</c> when every comparison the mode called for found the schemas compatible.</summary>
    public bool IsCompatible => Checks.All(c => c.Result.IsCompatible);
}
