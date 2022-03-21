namespace SJP.Avro.Tools.Idl.Model;

/// <summary>
/// Defines the possible options by which Avro definitions can be imported into an IDL document.
/// </summary>
public enum ImportType
{
    /// <summary>
    /// Invalid case.
    /// </summary>
    Unknown,

    /// <summary>
    /// A document containing Avro IDL.
    /// </summary>
    Idl,

    /// <summary>
    /// A document that contains a JSON definition of an Avro protocol.
    /// </summary>
    Protocol,

    /// <summary>
    /// A document that contains a JSON definition of Avro schema.
    /// </summary>
    Schema
}
