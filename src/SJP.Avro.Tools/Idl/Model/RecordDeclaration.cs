using System;
using System.Collections.Generic;

namespace SJP.Avro.Tools.Idl.Model;

/// <summary>
/// Describes an Avro record type.
/// </summary>
public record RecordDeclaration : NamedSchemaDeclaration
{
    /// <summary>
    /// Constructs a record type definition.
    /// </summary>
    /// <param name="comment">An (optional) documentation-level comment.</param>
    /// <param name="properties">A collection of properties to attach to the record declaration. Can be empty.</param>
    /// <param name="position">The position of the error within the source document.</param>
    /// <param name="name">The name of the record type.</param>
    /// <param name="fields">The set of fields that the record type contains.</param>
    /// <exception cref="ArgumentNullException"><paramref name="properties"/>, <paramref name="name"/>, or <paramref name="fields"/> are <c>null</c>.</exception>
    public RecordDeclaration(
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
    /// The name of the record type.
    /// </summary>
    public Identifier Name { get; }

    /// <summary>
    /// The set of fields that the record type defines.
    /// </summary>
    public IEnumerable<FieldDeclaration> Fields { get; }
}
