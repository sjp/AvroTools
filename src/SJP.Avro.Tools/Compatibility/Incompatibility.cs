using System;

namespace SJP.Avro.Tools.Compatibility;

/// <summary>
/// Describes a single reason why a writer schema cannot be read by a reader schema.
/// </summary>
public sealed class Incompatibility
{
    /// <summary>
    /// Initialises a new instance of the <see cref="Incompatibility"/> class.
    /// </summary>
    /// <param name="type">The kind of incompatibility.</param>
    /// <param name="message">A human-readable description of the incompatibility.</param>
    /// <param name="location">A path locating the incompatibility within the schema, e.g. <c>/fields/name/type</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> or <paramref name="location"/> is <c>null</c>.</exception>
    public Incompatibility(SchemaIncompatibilityType type, string message, string location)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(location);

        Type = type;
        Message = message;
        Location = location;
    }

    /// <summary>The kind of incompatibility.</summary>
    public SchemaIncompatibilityType Type { get; }

    /// <summary>A human-readable description of the incompatibility.</summary>
    public string Message { get; }

    /// <summary>A path locating the incompatibility within the schema, e.g. <c>/fields/name/type</c>.</summary>
    public string Location { get; }
}
