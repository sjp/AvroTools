using System;
using System.Collections.Generic;
using Avro;

namespace SJP.Avro.Tools.CodeGen;

/// <summary>
/// A default implementation of a code generator resolver.
/// </summary>
public class CodeGeneratorResolver : ICodeGeneratorResolver
{
    private static readonly IReadOnlyDictionary<Type, object> _generatorLookup = new Dictionary<Type, object>
    {
        [typeof(EnumSchema)] = new AvroEnumGenerator(),
        [typeof(FixedSchema)] = new AvroFixedGenerator(),
        [typeof(RecordSchema)] = new AvroRecordGenerator(),
        [typeof(Protocol)] = new AvroProtocolGenerator()
    };

    /// <summary>
    /// Retrieves a code generator matching a provided type.
    /// </summary>
    /// <typeparam name="T">The type of <see cref="Schema"/> or <see cref="Protocol"/> to get a code generator for.</typeparam>
    /// <returns>A code generator for the given type, otherwise <c>null</c></returns>
    public ICodeGenerator<T>? Resolve<T>()
    {
        return _generatorLookup.TryGetValue(typeof(T), out var generator)
            ? generator as ICodeGenerator<T>
            : null;
    }
}
