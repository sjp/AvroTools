using SJP.Avro.Tools.Idl.Model;

namespace SJP.Avro.Tools;

/// <summary>
/// Defines a compiler used to generate a JSON protocol from an Avro IDL protocol.
/// </summary>
public interface IIdlCompiler
{
    /// <summary>
    /// Compiles an IDL protocol into a JSON definition of the same protocol.
    /// </summary>
    /// <param name="filePath">A path representing the source location of <paramref name="protocol"/>.</param>
    /// <param name="protocol">A parsed protocol definition from an IDL document.</param>
    /// <returns>A string containing JSON text that represents an Avro protocol.</returns>
    string Compile(string filePath, Protocol protocol);
}