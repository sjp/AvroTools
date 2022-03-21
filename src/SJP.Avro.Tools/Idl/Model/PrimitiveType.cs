using System;
using System.Collections.Generic;

namespace SJP.Avro.Tools.Idl.Model;

/// <summary>
/// Defines an Avro primitive type, i.e. a well-known type name with no additional attributes required.
/// </summary>
public record PrimitiveType : AvroType
{
    /// <summary>
    /// Constructs a primitive type reference.
    /// </summary>
    /// <param name="name">The name of the primitive type.</param>
    /// <param name="properties">A collection of properties to attach to the primitive type. Can be empty.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <c>null</c>, empty or whitespace. Alternatively when <paramref name="properties"/> is <c>null</c>.</exception>
    public PrimitiveType(string name, IEnumerable<Property> properties)
        : base(properties)
    {
        if (name.IsNullOrWhiteSpace())
            throw new ArgumentNullException(nameof(name));

        Name = name;
    }

    /// <summary>
    /// The name of an Avro primitive type.
    /// </summary>
    public string Name { get; }
}
