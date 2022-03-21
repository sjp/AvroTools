using System;
using System.Collections.Generic;

namespace SJP.Avro.Tools.Idl.Model;

/// <summary>
/// Describes an Avro union, i.e. a type that can resolve to one of many different types.
/// </summary>
public record UnionDefinition : AvroType
{
    /// <summary>
    /// Constructs a union type definition.
    /// </summary>
    /// <param name="typeOptions">The set of possible types that the union can resolve to.</param>
    /// <param name="properties">Any properties associated with the union type.</param>
    /// <exception cref="ArgumentNullException"><paramref name="typeOptions"/> or <paramref name="properties"/> is <c>null</c>.</exception>
    public UnionDefinition(IEnumerable<AvroType> typeOptions, IEnumerable<Property> properties)
        : base(properties)
    {
        TypeOptions = typeOptions ?? throw new ArgumentNullException(nameof(typeOptions));
    }

    /// <summary>
    /// The set of possible types that the union can resolve to.
    /// </summary>
    public IEnumerable<AvroType> TypeOptions { get; }
}
