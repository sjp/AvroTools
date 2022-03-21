using System;
using System.Collections.Generic;

namespace SJP.Avro.Tools.Idl.Model;

/// <summary>
/// Describes an Avro error type.
/// </summary>
public record ErrorDeclaration : NamedSchemaDeclaration
{
    /// <summary>
    /// Constructs an error type definition.
    /// </summary>
    /// <param name="comment">An (optional) documentation-level comment.</param>
    /// <param name="properties">A collection of properties to attach to the error declaration. Can be empty.</param>
    /// <param name="position">The position of the error within the source document.</param>
    /// <param name="name">The name of the error type.</param>
    /// <param name="fields">The set of fields that the error type contains.</param>
    /// <exception cref="ArgumentNullException"><paramref name="properties"/>, <paramref name="name"/>, or <paramref name="fields"/> are <c>null</c>.</exception>
    public ErrorDeclaration(
        DocComment? comment,
        IEnumerable<Property> properties,
        int position,
        Identifier name,
        IEnumerable<FieldDeclaration> fields
    ) : base(comment, properties, position)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    /// <summary>
    /// The name of the error type.
    /// </summary>
    public Identifier Name { get; }

    /// <summary>
    /// The set of fields that the error type defines.
    /// </summary>
    public IEnumerable<FieldDeclaration> Fields { get; }
}
