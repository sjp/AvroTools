using System;
using System.Collections.Generic;

namespace SJP.Avro.Tools.Idl.Model;

/// <summary>
/// Describes a generic Avro type.
/// </summary>
public abstract record AvroType
{
    /// <summary>
    /// Constructs a generic Avro type definition with a set of known properties.
    /// </summary>
    /// <param name="properties">A collection of properties to attach to the given type. Can be empty.</param>
    /// <exception cref="ArgumentNullException"><paramref name="properties"/> is <c>null</c>.</exception>
    protected AvroType(IEnumerable<Property> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        Properties = properties;
    }

    /// <summary>
    /// A collection of properties that are attached to the given type.
    /// </summary>
    public IEnumerable<Property> Properties { get; }
}