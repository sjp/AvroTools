using System;
using System.Collections.Generic;

namespace SJP.Avro.Tools.Idl.Model;

/// <summary>
/// Describes an Avro logical type. i.e. a built-in Avro type with additional attributes compared to a primitive type.
/// </summary>
public record LogicalType : AvroType
{
    /// <summary>
    /// Constructs a logical type definition.
    /// </summary>
    /// <param name="name">The name of the logical type, e.g. 'decimal', 'uuid', 'timestamp-millis'.</param>
    /// <param name="properties">A collection of properties to attach to the logical type. Can be empty.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <c>null</c>. Alternatively when <paramref name="properties"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or whitespace.</exception>
    public LogicalType(string name, IEnumerable<Property> properties)
        : base(properties)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
    }

    /// <summary>
    /// The name of the logical type.
    /// </summary>
    public string Name { get; }
}