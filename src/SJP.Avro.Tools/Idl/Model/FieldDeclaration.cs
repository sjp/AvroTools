using System;
using System.Collections.Generic;
using Superpower.Model;

namespace SJP.Avro.Tools.Idl.Model
{
    /// <summary>
    /// Defines a field for a record or error type.
    /// </summary>
    public record FieldDeclaration
    {
        /// <summary>
        /// Constructs a record or error field definition.
        /// </summary>
        /// <param name="comment">An (optional) documentation-level comment.</param>
        /// <param name="properties">A collection of properties to attach to the field declaration. Can be empty.</param>
        /// <param name="type">The field type.</param>
        /// <param name="name">The field name.</param>
        /// <param name="defaultValue">The default value of the field declaration, if available.</param>
        /// <exception cref="ArgumentNullException"><paramref name="properties"/>, <paramref name="type"/>, <paramref name="name"/> or <paramref name="defaultValue"/> is <c>null</c>.</exception>
        public FieldDeclaration(
            DocComment? comment,
            IEnumerable<Property> properties,
            AvroType type,
            Identifier name,
            IEnumerable<Token<IdlToken>> defaultValue
        )
        {
            Comment = comment;
            Properties = properties ?? throw new ArgumentNullException(nameof(properties));
            Type = type ?? throw new ArgumentNullException(nameof(type));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            DefaultValue = defaultValue ?? throw new ArgumentNullException(nameof(defaultValue));
        }

        /// <summary>
        /// An (optional) documentation-level comment.
        /// </summary>
        public DocComment? Comment { get; }

        /// <summary>
        /// Properties that are attached to the given field.
        /// </summary>
        public IEnumerable<Property> Properties { get; }

        /// <summary>
        /// The type of the field.
        /// </summary>
        public AvroType Type { get; }

        /// <summary>
        /// The field name.
        /// </summary>
        public Identifier Name { get; }

        /// <summary>
        /// The default value for the field. An empty collection indicates no default value.
        /// </summary>
        public IEnumerable<Token<IdlToken>> DefaultValue { get; }
    }
}
