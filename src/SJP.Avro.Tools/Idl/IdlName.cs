using System;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// Contains helper methods for handling names used within IDL documents.
/// </summary>
public static class IdlName
{
    /// <summary>
    /// Escapes names if required.
    /// Raw IDL names may be escaped via <c>`</c> characters, which should be removed when converting to JSON protocols/schema.
    /// </summary>
    /// <param name="name">A name used in an IDL context, typically those that would map to a JSON property name.</param>
    /// <returns>A name, escaped if needed.</returns>
    public static string EscapeName(string name)
    {
        return TryGetNamespaceNamePairing(name, out var namespacePair)
            ? $"{namespacePair.Namespace}.{EscapeLocalName(namespacePair.Name)}"
            : EscapeLocalName(name);
    }

    private static string EscapeLocalName(string name)
    {
        var builtInName = name.TrimStart('`').TrimEnd('`');
        return LanguageKeywords.Contains(builtInName)
            ? builtInName
            : name;
    }

    private static bool TryGetNamespaceNamePairing(string name, out (string Namespace, string Name) namespacePair)
    {
        if (!name.Contains('.'))
        {
            namespacePair = (string.Empty, string.Empty);
            return false;
        }

        var pieces = name.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ns = string.Join('.', pieces[0..^1]);
        var localName = pieces[^1];

        namespacePair = (ns, localName);
        return true;
    }

    private static readonly string[] LanguageKeywords =
    [
        "protocol",
        "namespace",
        "import",
        "idl",
        "schema",
        "throws",
        "oneway",
        "error",

        // type declarations
        "record",
        "enum",
        "fixed",
        "array",
        "map",
        "union",

        // primitive types
        "boolean",
        "int",
        "long",
        "float",
        "double",
        "bytes",
        "string",
        "null",
        "void",

        // logical types
        "decimal",
        "date",
        "time_ms",
        "timestamp_ms",
        "local_timestamp_ms",
        "uuid",
    ];
}
