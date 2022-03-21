using System;
using System.Collections.Generic;

namespace SJP.Avro.Tools.Idl.Model;

/// <summary>
/// Defines a reference type, i.e. a type that refers to another type by name.
/// </summary>
public record ReferenceType : AvroType
{
    /// <summary>
    /// Constructs a reference type definition.
    /// </summary>
    /// <param name="name">The name of the type being referenced.</param>
    /// <param name="properties">A collection of properties to attach to the referenced type. Can be empty.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="properties"/> are <c>null</c>.</exception>
    public ReferenceType(Identifier name, IEnumerable<Property> properties)
        : base(properties)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>
    /// The name of the reference type.
    /// </summary>
    public Identifier Name { get; }
}