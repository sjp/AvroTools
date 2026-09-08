using System;

namespace SJP.Avro.Tools.Diff;

/// <summary>
/// Describes a single semantic difference found between a "before" and an "after" Avro schema.
/// </summary>
public sealed class SchemaChange
{
    /// <summary>
    /// Initialises a new instance of the <see cref="SchemaChange"/> class.
    /// </summary>
    /// <param name="kind">The kind of change.</param>
    /// <param name="location">A path locating the change within the schema, e.g. <c>/fields/name/type</c>.</param>
    /// <param name="message">A human-readable description of the change.</param>
    /// <param name="oldValue">The prior value, when applicable to <paramref name="kind"/>; otherwise <c>null</c>.</param>
    /// <param name="newValue">The new value, when applicable to <paramref name="kind"/>; otherwise <c>null</c>.</param>
    /// <param name="isValidPromotion">Whether a reader using the new type can read data written with the old type under Avro's type promotion rules; set only for type-changing kinds.</param>
    /// <exception cref="ArgumentNullException"><paramref name="location"/> or <paramref name="message"/> is <c>null</c>.</exception>
    public SchemaChange(
        ChangeKind kind,
        string location,
        string message,
        string? oldValue = null,
        string? newValue = null,
        bool? isValidPromotion = null)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(message);

        Kind = kind;
        Location = location;
        Message = message;
        OldValue = oldValue;
        NewValue = newValue;
        IsValidPromotion = isValidPromotion;
    }

    /// <summary>The kind of change.</summary>
    public ChangeKind Kind { get; }

    /// <summary>A path locating the change within the schema, e.g. <c>/fields/name/type</c>.</summary>
    public string Location { get; }

    /// <summary>A human-readable description of the change.</summary>
    public string Message { get; }

    /// <summary>The prior value, when applicable to <see cref="Kind"/>; otherwise <c>null</c>.</summary>
    public string? OldValue { get; }

    /// <summary>The new value, when applicable to <see cref="Kind"/>; otherwise <c>null</c>.</summary>
    public string? NewValue { get; }

    /// <summary>
    /// Whether a reader using the new type can read data written with the old type under Avro's
    /// type promotion rules; set only for type-changing kinds. Promotion is directional, so a
    /// change from <c>int</c> to <c>long</c> is a valid promotion while <c>long</c> to <c>int</c>
    /// is not.
    /// </summary>
    public bool? IsValidPromotion { get; }
}
