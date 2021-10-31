using System;
using System.Collections.Generic;

namespace SJP.Avro.Tools.Idl.Model
{
    /// <summary>
    /// Describes an Avro fixed type.
    /// </summary>
    public record FixedDeclaration : NamedSchemaDeclaration
    {
        /// <summary>
        /// Constructs a fixed type definition.
        /// </summary>
        /// <param name="comment">An (optional) documentation-level comment.</param>
        /// <param name="properties">A collection of properties to attach to the fixed type declaration. Can be empty.</param>
        /// <param name="position">The position of the fixed type within the source document.</param>
        /// <param name="name">The name of the fixed type.</param>
        /// <param name="size">The number of bytes that the fixed sized type will occupy per instance.</param>
        /// <exception cref="ArgumentNullException"><paramref name="properties"/> or <paramref name="name"/> are <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is less than one.</exception>
        public FixedDeclaration(
            DocComment? comment,
            IEnumerable<Property> properties,
            int position,
            Identifier name,
            int size
        ) : base(comment, properties, position)
        {
            if (size < 1)
                throw new ArgumentOutOfRangeException(nameof(size), $"A fixed type must have a size of at least zero. Given: '{ size }'.");

            Name = name ?? throw new ArgumentNullException(nameof(name));
            Size = size;
        }

        /// <summary>
        /// The name of the fixed type.
        /// </summary>
        public Identifier Name { get; }

        /// <summary>
        /// The number of bytes that the fixed sized type will occupy per instance.
        /// </summary>
        public int Size { get; }
    }
}
