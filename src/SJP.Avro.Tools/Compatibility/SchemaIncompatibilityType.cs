namespace SJP.Avro.Tools.Compatibility;

/// <summary>
/// The kinds of schema incompatibility that can be detected when checking whether
/// data written with one schema can be read with another. Mirrors the incompatibility
/// classification used by the Java <c>SchemaCompatibility</c> API so results are familiar.
/// </summary>
public enum SchemaIncompatibilityType
{
    /// <summary>A named type (record, enum or fixed) has a name that the reader does not recognise, even accounting for aliases.</summary>
    NameMismatch,

    /// <summary>Two <c>fixed</c> types share a name but declare a different number of bytes.</summary>
    FixedSizeMismatch,

    /// <summary>The writer can produce enum symbols that the reader does not declare, and the reader has no enum default to fall back on.</summary>
    MissingEnumSymbols,

    /// <summary>The reader declares a field that is absent from the writer and has no default value, so it cannot be populated.</summary>
    ReaderFieldMissingDefaultValue,

    /// <summary>The reader and writer types differ and the writer type is not promotable to the reader type.</summary>
    TypeMismatch,

    /// <summary>The reader is a union that has no branch capable of reading a type the writer can produce.</summary>
    MissingUnionBranch,
}
