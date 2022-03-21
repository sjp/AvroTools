using System;
using System.Collections.Generic;
using Superpower.Model;

namespace SJP.Avro.Tools.Idl.Model;

/// <summary>
/// An Avro property definition.
/// </summary>
public record Property
{
    private const string PropertyPrefix = "@";

    /// <summary>
    /// Constructs a property definition.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <param name="value">The value of the property.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <c>null</c>, empty or whitespace. Alternatively if <paramref name="value"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> does not start with '@'.</exception>
    public Property(string name, IEnumerable<Token<IdlToken>> value)
    {
        if (name.IsNullOrWhiteSpace())
            throw new ArgumentNullException(nameof(name));
        if (!name.StartsWith(PropertyPrefix))
            throw new ArgumentException($"The given property name '{ name }' does not start with '{ PropertyPrefix }'.", nameof(name));

        Name = TrimNamespacePrefix(name);
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// The property name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The value of the property. A series of tokens as this can be (roughly speaking) any type.
    /// </summary>
    public IEnumerable<Token<IdlToken>> Value { get; }

    private static string TrimNamespacePrefix(string name) => name[PropertyPrefix.Length..];
}
