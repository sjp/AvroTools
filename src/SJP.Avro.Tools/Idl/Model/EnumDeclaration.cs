using System;
using System.Collections.Generic;

namespace SJP.Avro.Tools.Idl.Model
{
    /// <summary>
    /// Describes an Avro enumeration type.
    /// </summary>
    public record EnumDeclaration : NamedSchemaDeclaration
    {
        /// <summary>
        /// Constructs an enumeration type definition.
        /// </summary>
        /// <param name="comment">An (optional) documentation-level comment.</param>
        /// <param name="properties">A collection of properties to attach to the enum declaration. Can be empty.</param>
        /// <param name="position">The position of the enum within the source document.</param>
        /// <param name="name">The name of the enum type.</param>
        /// <param name="members">The set of members of the enum type.</param>
        /// <param name="defaultValue">An (optional) default value to use for the enum type when a value is not available.</param>
        /// <exception cref="ArgumentNullException"><paramref name="properties"/>, <paramref name="name"/>, or <paramref name="members"/> are <c>null</c>.</exception>
        public EnumDeclaration(
            DocComment? comment,
            IEnumerable<Property> properties,
            int position,
            Identifier name,
            IEnumerable<Identifier> members,
            Identifier? defaultValue = null
        ) : base(comment, properties, position)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Members = members ?? throw new ArgumentNullException(nameof(members));
            DefaultValue = defaultValue;
        }

        /// <summary>
        /// The name of the enum type.
        /// </summary>
        public Identifier Name { get; }

        /// <summary>
        /// The set of possible values defined by enum type.
        /// </summary>
        public IEnumerable<Identifier> Members { get; }

        /// <summary>
        /// The default value of the enum type when a value is not available. Must be in the set of <see cref="Members"/>.
        /// </summary>
        public Identifier? DefaultValue { get; }
    }
}
