using System;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// Contains helper methods for handling names used within IDL documents.
/// </summary>
public static class IdlName
{
    /// <summary>
    /// Escapes names if required.
    /// Raw IDL names may be escaped via <c>`</c> characters, which should be removed when converting to JSON protocols/schema.
    /// </summary>
    /// <param name="name">A name used in an IDL context, typically those that would map to a JSON property name.</param>
    /// <returns>A name, escaped if needed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <c>null</c>.</exception>
    public static string EscapeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        // Backticks only ever wrap a single part of an identifier, so they can
        // always be removed without affecting the separators between parts.
        return name.Contains('`', StringComparison.Ordinal)
            ? name.Replace("`", string.Empty, StringComparison.Ordinal)
            : name;
    }
}
