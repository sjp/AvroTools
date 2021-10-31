using Avro;

namespace SJP.Avro.Tools.CodeGen
{
    /// <summary>
    /// Defines a code generator resolver. Intended to be used to look up a given resolver by type when provided.
    /// </summary>
    public interface ICodeGeneratorResolver
    {
        /// <summary>
        /// Retrieves a code generator matching a provided type.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="Schema"/> or <see cref="Protocol"/> to get a code generator for.</typeparam>
        /// <returns>A code generator for the given type, otherwise <c>null</c></returns>
        ICodeGenerator<T>? Resolve<T>();
    }
}
