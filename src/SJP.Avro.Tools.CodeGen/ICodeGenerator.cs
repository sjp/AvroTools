using Avro;

namespace SJP.Avro.Tools.CodeGen
{
    /// <summary>
    /// Defines a code generator for a given type.
    /// Intended to be used to generate code for non-abstract instances of <see cref="Schema"/> or <see cref="Protocol"/>
    /// </summary>
    /// <typeparam name="T">The type of <see cref="Schema"/> or <see cref="Protocol"/> to generate code for.</typeparam>
    public interface ICodeGenerator<in T>
    {
        /// <summary>
        /// Creates a C# implementation of an Avro data type (schema or protocol).
        /// </summary>
        /// <param name="source">A definition of Avro source schema or protocol.</param>
        /// <param name="baseNamespace">The base namespace to use (when one is absent).</param>
        /// <returns>A string representing a C# file containing a definition of the provided source.</returns>
        string Generate(T source, string baseNamespace);
    }
}
