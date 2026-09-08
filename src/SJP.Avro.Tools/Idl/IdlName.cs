using System;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// Contains helper methods for handling names used within IDL documents.
/// </summary>
public static class IdlName
{
    /// <summary>
    /// Removes the escaping from a name.
    /// Raw IDL names may wrap parts of a name in <c>`</c> characters so that a word which would
    /// otherwise be read as a keyword can be used; the backticks are not part of the name and must
    /// be removed when converting to JSON protocols/schema.
    /// </summary>
    /// <param name="name">A name used in an IDL context, typically those that would map to a JSON property name.</param>
    /// <returns>The name without any escaping.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <c>null</c>.</exception>
    public static string Unescape(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        // Backticks only ever wrap a single part of an identifier, so they can
        // always be removed without affecting the separators between parts.
        return name.Contains('`', StringComparison.Ordinal)
            ? name.Replace("`", string.Empty, StringComparison.Ordinal)
            : name;
    }
}
