namespace SJP.Avro.Tools.Compatibility;

/// <summary>
/// The schema-registry compatibility modes that describe which evolution direction(s) must hold.
/// </summary>
public enum CompatibilityMode
{
    /// <summary>A new reader can read data written with the old writer.</summary>
    Backward,

    /// <summary>An old reader can read data written with the new writer.</summary>
    Forward,

    /// <summary>Both <see cref="Backward"/> and <see cref="Forward"/> hold.</summary>
    Full,

    /// <summary><see cref="Backward"/> holds against every earlier version in a chain, not only the most recent.</summary>
    BackwardTransitive,

    /// <summary><see cref="Forward"/> holds against every earlier version in a chain, not only the most recent.</summary>
    ForwardTransitive,

    /// <summary><see cref="Full"/> holds against every earlier version in a chain, not only the most recent.</summary>
    FullTransitive,
}
