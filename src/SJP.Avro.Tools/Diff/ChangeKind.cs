namespace SJP.Avro.Tools.Diff;

/// <summary>
/// The kinds of semantic change that can be detected between two versions of an Avro schema.
/// </summary>
public enum ChangeKind
{
    /// <summary>A field is present in the "after" schema but not in the "before" schema.</summary>
    FieldAdded,

    /// <summary>A field is present in the "before" schema but not in the "after" schema.</summary>
    FieldRemoved,

    /// <summary>A field was matched across schemas via an alias, and its name differs between them.</summary>
    FieldRenamed,

    /// <summary>A matched field's type changed between schemas.</summary>
    FieldTypeChanged,

    /// <summary>A matched field gained a default value it did not previously have.</summary>
    FieldDefaultAdded,

    /// <summary>A matched field lost a default value it previously had.</summary>
    FieldDefaultRemoved,

    /// <summary>A matched field's default value changed.</summary>
    FieldDefaultChanged,

    /// <summary>An enum gained one or more symbols.</summary>
    EnumSymbolAdded,

    /// <summary>An enum lost one or more symbols.</summary>
    EnumSymbolRemoved,

    /// <summary>An enum declares the same symbols in both schemas, but in a different order.</summary>
    EnumSymbolsReordered,

    /// <summary>A <c>fixed</c> type's declared size changed.</summary>
    FixedSizeChanged,

    /// <summary>A union gained a branch.</summary>
    UnionBranchAdded,

    /// <summary>A union lost a branch.</summary>
    UnionBranchRemoved,

    /// <summary>
    /// The schema at a location was replaced by a structurally different type: either the Avro
    /// type tag differs (e.g. record vs enum), or both sides are named schemas with different
    /// names and no alias linking them.
    /// </summary>
    TypeKindChanged,

    /// <summary>
    /// A change to metadata that does not affect the schema's shape (documentation, aliases, or
    /// an enum's default symbol). Only reported when metadata comparison is requested.
    /// </summary>
    MetadataChanged,
}
