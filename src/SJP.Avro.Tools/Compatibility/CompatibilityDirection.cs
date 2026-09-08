namespace SJP.Avro.Tools.Compatibility;

/// <summary>
/// The evolution direction that a single reader/writer check covers.
/// </summary>
public enum CompatibilityDirection
{
    /// <summary>The candidate schema is the reader, and must read data written with an earlier schema.</summary>
    Backward,

    /// <summary>The candidate schema is the writer, and its data must be read by an earlier schema.</summary>
    Forward,
}
