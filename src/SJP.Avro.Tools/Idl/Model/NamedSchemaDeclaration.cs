using System;
using System.Collections.Generic;

namespace SJP.Avro.Tools.Idl.Model;

/// <summary>
/// A named schema declaration. Simply a declaration of a type that has a given name (and possibly namespace).
/// </summary>
public abstract record NamedSchemaDeclaration
{
    /// <summary>
    /// Constructs a generic named schema declaration.
    /// </summary>
    /// <param name="comment">An (optional) documentation-level comment.</param>
    /// <param name="properties">A collection of properties to attach to the schema declaration. Can be empty.</param>
    /// <param name="position">The position of the declaration within the source document.</param>
    /// <exception cref="ArgumentNullException"><paramref name="properties"/> is <c>null</c>.</exception>
    protected NamedSchemaDeclaration(DocComment? comment, IEnumerable<Property> properties, int position)
    {
        Comment = comment;
        Properties = properties ?? throw new ArgumentNullException(nameof(properties));
        Position = position;
    }

    /// <summary>
    /// An (optional) documentation-level comment.
    /// </summary>
    public DocComment? Comment { get; }

    /// <summary>
    /// A collection of properties that are attached to the given declaration.
    /// </summary>
    public IEnumerable<Property> Properties { get; }

    /// <summary>
    /// The position of the declaration in the source document.
    /// Not relevant aside from enabling ordering of results.
    /// </summary>
    public int Position { get; }
}
