namespace AvroTool;

/// <summary>
/// The exit codes the tool reports.
/// </summary>
/// <remarks>
/// The comparison commands, <c>compat</c> and <c>diff</c>, answer a yes/no question about two
/// schemas, and follow the convention <c>diff(1)</c> and <c>grep(1)</c> set: zero when the
/// answer is yes, one when it is no, and two when the question could not be answered at all.
/// A build gated on the answer can then tell an incompatible schema from a missing file, which
/// a single non-zero code cannot express.
/// </remarks>
internal static class ErrorCode
{
    /// <summary>The command did what was asked of it.</summary>
    public static int Success { get; }

    /// <summary>The command could not do what was asked of it.</summary>
    public static int Error { get; } = 1;

    /// <summary>A comparison command ran, and the schemas are incompatible or differ.</summary>
    public static int Difference { get; } = 1;

    /// <summary>A comparison command could not reach an answer at all.</summary>
    public static int ComparisonError { get; } = 2;
}
