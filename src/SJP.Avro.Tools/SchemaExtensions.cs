using System.Collections.Generic;
using System.Linq;
using Avro;

namespace SJP.Avro.Tools;

/// <summary>
/// Extension methods used to aid gathering data from existing Avro schemas.
/// </summary>
public static class SchemaExtensions
{
    /// <summary>
    /// Retrieves all named types, e.g. records, fixed, enums, from a schema,
    /// including nested types that are inlined in the schema definition.
    /// </summary>
    public static IEnumerable<NamedSchema> GetNamedTypes(this Schema schema)
    {
        var visitedTypes = new HashSet<string>();
        return ExtractAllNamedTypesRecursive(schema, visitedTypes).ToList();
    }

    private static IEnumerable<NamedSchema> ExtractAllNamedTypesRecursive(Schema schema, HashSet<string> visitedTypes)
    {
        switch (schema)
        {
            case RecordSchema recordSchema:
                foreach (var recordExtractedType in ExtractRecordSchemaTypes(recordSchema, visitedTypes))
                    yield return recordExtractedType;
                break;

            case EnumSchema enumSchema:
                var enumFullName = enumSchema.Fullname;
                if (!visitedTypes.Add(enumFullName))
                    yield break;

                yield return enumSchema;
                break;

            case FixedSchema fixedSchema:
                var fixedFullName = fixedSchema.Fullname;
                if (!visitedTypes.Add(fixedFullName))
                    yield break;

                yield return fixedSchema;
                break;

            case ArraySchema arraySchema:
                foreach (var nestedType in ExtractAllNamedTypesRecursive(arraySchema.ItemSchema, visitedTypes))
                    yield return nestedType;
                break;

            case MapSchema mapSchema:
                foreach (var nestedType in ExtractAllNamedTypesRecursive(mapSchema.ValueSchema, visitedTypes))
                    yield return nestedType;
                break;

            case UnionSchema unionSchema:
                foreach (var unionExtractedType in ExtractUnionSchemaTypes(unionSchema, visitedTypes))
                    yield return unionExtractedType;
                break;

            // primitives and other types have no named types to extract
            default:
                yield break;
        }
    }

    private static IEnumerable<NamedSchema> ExtractRecordSchemaTypes(RecordSchema record, HashSet<string> visitedTypes)
    {
        var recordFullName = record.Fullname;
        if (!visitedTypes.Add(recordFullName))
            yield break;

        yield return record;

        var fieldTypes = record
            .Fields
            .SelectMany(f => ExtractAllNamedTypesRecursive(f.Schema, visitedTypes));
        foreach (var nestedType in fieldTypes)
            yield return nestedType;
    }

    private static IEnumerable<NamedSchema> ExtractUnionSchemaTypes(UnionSchema union, HashSet<string> visitedTypes)
    {
        return union
            .Schemas
            .SelectMany(s => ExtractAllNamedTypesRecursive(s, visitedTypes));
    }
}