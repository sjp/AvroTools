using System;
using System.Collections.Generic;

namespace SJP.Avro.Tools.Idl.Model;

/// <summary>
/// Describes an Avro message.
/// </summary>
public record MessageDeclaration : NamedSchemaDeclaration
{
    /// <summary>
    /// Constructs a message definition.
    /// </summary>
    /// <param name="comment">A documentation-level comment.</param>
    /// <param name="name">The name of the message.</param>
    /// <param name="returnType">The return type of the message.</param>
    /// <param name="properties">Properties to associate with the message.</param>
    /// <param name="position">The position of the message in the source file.</param>
    /// <param name="parameters">The set of parameters required to send the message.</param>
    /// <param name="oneway">Whether the message has no response type.</param>
    /// <param name="errors">The set of error responses that can be returned by the message invocation.</param>
    /// <exception cref="ArgumentNullException">Any of <paramref name="name"/>, <paramref name="returnType"/>, <paramref name="properties"/>, <paramref name="parameters"/>, <paramref name="errors"/> is <c>null</c>.</exception>
    public MessageDeclaration(
        DocComment? comment,
        Identifier name,
        AvroType returnType,
        IEnumerable<Property> properties,
        int position,
        IEnumerable<FormalParameter> parameters,
        bool oneway,
        IEnumerable<Identifier> errors
    ) : base(comment, properties, position)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ReturnType = returnType ?? throw new ArgumentNullException(nameof(returnType));
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        OneWay = oneway;
        Errors = errors ?? throw new ArgumentNullException(nameof(errors));
    }

    /// <summary>
    /// The name of the message.
    /// </summary>
    public Identifier Name { get; }

    /// <summary>
    /// The return type expected when passing the message.
    /// </summary>
    public AvroType ReturnType { get; }

    /// <summary>
    /// The set of parameters required to send the message.
    /// </summary>
    public IEnumerable<FormalParameter> Parameters { get; }

    /// <summary>
    /// Whether the message has no response type. If <c>true</c>, there is no response expected from the message.
    /// </summary>
    public bool OneWay { get; }

    /// <summary>
    /// The set of error responses that can be returned by the message invocation.
    /// </summary>
    public IEnumerable<Identifier> Errors { get; }
}