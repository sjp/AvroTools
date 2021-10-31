using System;
using System.Collections.Generic;

namespace SJP.Avro.Tools.Idl.Model
{
    /// <summary>
    /// Describes an Avro map, i.e. a lookup from a string to an object of a known type.
    /// </summary>
    public record MapType : AvroType
    {
        /// <summary>
        /// Constructs a map type definition.
        /// </summary>
        /// <param name="nestedType">The type of the map value.</param>
        /// <param name="properties">Any properties associated with the map type.</param>
        /// <exception cref="ArgumentNullException"><paramref name="nestedType"/> or <paramref name="properties"/> is <c>null</c>.</exception>
        public MapType(AvroType nestedType, IEnumerable<Property> properties)
            : base(properties)
        {
            NestedType = nestedType ?? throw new ArgumentNullException(nameof(nestedType));
        }

        /// <summary>
        /// The type of the map values. Can also be an map type (i.e. nested map).
        /// </summary>
        public AvroType NestedType { get; }
    }
}
