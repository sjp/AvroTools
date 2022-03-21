using System;
using System.Collections.Generic;

namespace SJP.Avro.Tools.Idl.Model;

/// <summary>
/// Describes an Avro array, i.e. a linear block of homogeneous data.
/// </summary>
public record ArrayType : AvroType
{
    /// <summary>
    /// Constructs an array type definition.
    /// </summary>
    /// <param name="nestedType">The type of the array elements.</param>
    /// <param name="properties">Any properties associated with the array type.</param>
    /// <exception cref="ArgumentNullException"><paramref name="nestedType"/> or <paramref name="properties"/> is <c>null</c>.</exception>
    public ArrayType(AvroType nestedType, IEnumerable<Property> properties)
        : base(properties)
    {
        NestedType = nestedType ?? throw new ArgumentNullException(nameof(nestedType));
    }

    /// <summary>
    /// The type of the array elements. Can also be an array type (i.e. multi-dimensional array).
    /// </summary>
    public AvroType NestedType { get; }
}
