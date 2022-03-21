using System;

namespace SJP.Avro.Tools.Idl.Model;

/// <summary>
/// Defines an Avro identifier value.
/// </summary>
public record Identifier
{
    /// <summary>
    /// Constructs an Avro identifier.
    /// </summary>
    /// <param name="identifier">The value of the identifier.</param>
    /// <exception cref="ArgumentNullException"><paramref name="identifier"/> is <c>null</c>, empty or whitespace.</exception>
    public Identifier(string identifier)
    {
        if (identifier.IsNullOrWhiteSpace())
            throw new ArgumentNullException(nameof(identifier));

        Value = StripQuoting(identifier);
    }

    /// <summary>
    /// The value of the identifier, with any required quoting characters removed.
    /// </summary>
    public string Value { get; }

    private static string StripQuoting(string identifier)
    {
        if (identifier.IsNullOrWhiteSpace())
            throw new ArgumentNullException(nameof(identifier));

        var isQuoted = identifier.StartsWith('`') && identifier.EndsWith('`');
        return isQuoted
            ? identifier[1..^1]
            : identifier;
    }
}