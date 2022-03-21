using System;
using System.Collections.Generic;

namespace SJP.Avro.Tools.Idl.Model;

/// <summary>
/// Describes an Avro protocol definition.
/// </summary>
public record Protocol
{
    /// <summary>
    /// Constructs an Avro protocol definition.
    /// </summary>
    /// <param name="doc">An (optional) documentation-level comment.</param>
    /// <param name="name">The name of the protocol.</param>
    /// <param name="properties">Properties to attach to the protocol.</param>
    /// <param name="records">Record types defined in the protocol.</param>
    /// <param name="fixeds">Fixed types defined in the protocol.</param>
    /// <param name="enums">Enumeration types defined in the prtocol.</param>
    /// <param name="errors">Error types defined in the protocol.</param>
    /// <param name="imports">A collection of imported document references.</param>
    /// <param name="messages">A collection of messages defined in the protocol.</param>
    /// <exception cref="ArgumentNullException">Any of <paramref name="name"/>, <paramref name="properties"/>, <paramref name="records"/>, <paramref name="fixeds"/>, <paramref name="enums"/>, <paramref name="errors"/>, <paramref name="imports"/>, <paramref name="messages"/> are <c>null</c>.</exception>
    public Protocol(
        DocComment? doc,
        Identifier name,
        IEnumerable<Property> properties,
        IEnumerable<RecordDeclaration> records,
        IEnumerable<FixedDeclaration> fixeds,
        IEnumerable<EnumDeclaration> enums,
        IEnumerable<ErrorDeclaration> errors,
        IEnumerable<ImportDeclaration> imports,
        IReadOnlyDictionary<Identifier, MessageDeclaration> messages)
    {
        Documentation = doc;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Properties = properties ?? throw new ArgumentNullException(nameof(properties));
        Records = records ?? throw new ArgumentNullException(nameof(records));
        Fixeds = fixeds ?? throw new ArgumentNullException(nameof(fixeds));
        Enums = enums ?? throw new ArgumentNullException(nameof(enums));
        Errors = errors ?? throw new ArgumentNullException(nameof(errors));
        Imports = imports ?? throw new ArgumentNullException(nameof(imports));
        Messages = messages ?? throw new ArgumentNullException(nameof(messages));
    }

    /// <summary>
    /// An (optional) documentation-level comment.
    /// </summary>
    public DocComment? Documentation { get; }

    /// <summary>
    /// The name of the protocol.
    /// </summary>
    public Identifier Name { get; }

    /// <summary>
    /// A collection of properties that are attached to the given protocol.
    /// </summary>
    public IEnumerable<Property> Properties { get; }

    /// <summary>
    /// The set of record types that the protocol defines.
    /// </summary>
    public IEnumerable<RecordDeclaration> Records { get; }

    /// <summary>
    /// The set of fixed types that the protocol defines.
    /// </summary>
    public IEnumerable<FixedDeclaration> Fixeds { get; }

    /// <summary>
    /// The set of enumeration types that the protocol defines.
    /// </summary>
    public IEnumerable<EnumDeclaration> Enums { get; }

    /// <summary>
    /// The set of error types that the protocol defines.
    /// </summary>
    public IEnumerable<ErrorDeclaration> Errors { get; }

    /// <summary>
    /// The set of imported documents that the protocol contains.
    /// </summary>
    public IEnumerable<ImportDeclaration> Imports { get; }

    /// <summary>
    /// The set of messages that the protocol defines.
    /// </summary>
    public IReadOnlyDictionary<Identifier, MessageDeclaration> Messages { get; }
}
