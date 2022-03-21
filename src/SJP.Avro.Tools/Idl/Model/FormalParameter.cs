using System;
using System.Collections.Generic;
using Superpower.Model;

namespace SJP.Avro.Tools.Idl.Model;

/// <summary>
/// Defines a formal parameter used for an Avro message.
/// </summary>
public record FormalParameter
{
    /// <summary>
    /// Constructs a message parameter definition.
    /// </summary>
    /// <param name="type">The type of the message parameter.</param>
    /// <param name="name">The name of the message parameter.</param>
    /// <param name="defaultValue">The default value of the message parameter, if available.</param>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> or <paramref name="name"/> or <paramref name="defaultValue"/> is <c>null</c>.</exception>
    public FormalParameter(
        AvroType type,
        Identifier name,
        IEnumerable<Token<IdlToken>> defaultValue
    )
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        DefaultValue = defaultValue ?? throw new ArgumentNullException(nameof(defaultValue));
    }

    /// <summary>
    /// The message parameter type.
    /// </summary>
    public AvroType Type { get; }

    /// <summary>
    /// The name of the message parameter.
    /// </summary>
    public Identifier Name { get; }

    /// <summary>
    /// The default value of the given parameter. If empty, means that no default value is present.
    /// </summary>
    public IEnumerable<Token<IdlToken>> DefaultValue { get; }
}
