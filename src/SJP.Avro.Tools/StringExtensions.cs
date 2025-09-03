using System;
using System.Collections.Generic;

namespace SJP.Avro.Tools;

/// <summary>
/// Convenience extension methods for working with <see cref="string"/> objects.
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// Concatenates the members of a constructed <see cref="IEnumerable{String}"/>, using the specified separator between each member.
    /// </summary>
    /// <param name="values">A collection that contains the strings to concatenate.</param>
    /// <param name="separator">The string to use as a separator. <paramref name="separator"/> is included in the returned string only if values has more than one element.</param>
    /// <returns>A string that consists of the members of <paramref name="values"/> delimited by the <paramref name="separator"/> string. Alternatively <c>""</c> if <paramref name="values"/> has zero elements.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> or <paramref name="separator"/> is <c>null</c></exception>
    public static string Join(this IEnumerable<string> values, string separator)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(separator);

        return string.Join(separator, values);
    }
}