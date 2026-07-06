namespace SJP.Avro.Tools.CodeGen;

/// <summary>
/// Controls optional C# output styles that a code generator may apply.
/// </summary>
/// <param name="RequiredProperties">
/// When <c>true</c>, non-optional properties (those without an Avro-declared
/// default and not a nullable union) are marked with the <c>required</c> modifier.
/// </param>
/// <param name="InitOnlyProperties">
/// When <c>true</c>, generated properties use <c>init</c> instead of <c>set</c>
/// accessors. A private backing field is used so that <c>ISpecificRecord.Put</c>
/// can still populate the instance after construction.
/// </param>
public sealed record CodeGenOptions(bool RequiredProperties = false, bool InitOnlyProperties = false)
{
    /// <summary>
    /// The default set of options, preserving today's generator output.
    /// </summary>
    public static CodeGenOptions Default { get; } = new();
}
