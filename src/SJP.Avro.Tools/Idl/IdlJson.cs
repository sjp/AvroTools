using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// Reads named-type structure directly from Avro JSON, resolving references the same way the Avro
/// object model does. Operating on the JSON directly, rather than on a parsed <c>Avro.Schema</c> or
/// <c>Avro.Protocol</c>, means a property the object model does not know how to round-trip is never
/// lost along the way.
/// </summary>
public static class IdlJson
{
    private static readonly FrozenSet<string> NamedSchemaKinds = new HashSet<string>(StringComparer.Ordinal) { "record", "error", "enum", "fixed" }.ToFrozenSet();

    /// <summary>
    /// The fully qualified name a named schema (a record, error, enum or fixed type) is written
    /// under: its own <c>name</c> when that is already dotted, otherwise <c>name</c> qualified by
    /// its <c>namespace</c> property, if it has one.
    /// </summary>
    public static string GetFullName(JObject namedSchema)
    {
        ArgumentNullException.ThrowIfNull(namedSchema);

        var name = namedSchema.Value<string>("name");
        if (string.IsNullOrEmpty(name))
            throw new InvalidOperationException("The schema has no 'name' property.");

        if (name.Contains('.', StringComparison.Ordinal))
            return name;

        var ns = namedSchema.Value<string>("namespace");
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    /// <summary>
    /// The namespace implied by a full name: everything before the last '.', or <c>null</c> for an
    /// unqualified name.
    /// </summary>
    private static string? GetNamespace(string fullName)
    {
        var lastSeparator = fullName.LastIndexOf('.');
        return lastSeparator >= 0 ? fullName[..lastSeparator] : null;
    }

    /// <summary>
    /// Resolves a type reference exactly as the Avro object model would: an already-dotted name is
    /// looked up as-is; an unqualified one is first tried against <paramref name="namespaceContext"/>
    /// (the namespace of the record or field the reference appears in), then as a bare name.
    /// </summary>
    private static bool TryResolveNamedSchema(
        string name,
        string? namespaceContext,
        IReadOnlyDictionary<string, JObject> namedSchemas,
        out string fullName,
        out JObject schema)
    {
        if (name.Contains('.', StringComparison.Ordinal))
        {
            fullName = name;
            return namedSchemas.TryGetValue(name, out schema!);
        }

        if (!string.IsNullOrEmpty(namespaceContext) && namedSchemas.TryGetValue($"{namespaceContext}.{name}", out schema!))
        {
            fullName = $"{namespaceContext}.{name}";
            return true;
        }

        fullName = name;
        return namedSchemas.TryGetValue(name, out schema!);
    }

    /// <summary>
    /// Finds every named type reachable from <paramref name="root"/>, in the order the Avro object
    /// model would discover them: <paramref name="root"/> itself when it is a named type, then each
    /// of its fields' types in turn (recursing through arrays, maps and unions), resolving a bare
    /// reference to another named type against <paramref name="namedSchemas"/>. Each distinct full
    /// name is visited once.
    /// </summary>
    public static IReadOnlyList<JObject> GetNamedTypes(JToken root, IReadOnlyDictionary<string, JObject> namedSchemas)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(namedSchemas);

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<JObject>();
        Walk(root, namedSchemas, visited, results, namespaceContext: null);
        return results;
    }

    /// <summary>
    /// The schema kind named by a JSON schema object's <c>type</c> property, or <c>null</c> when
    /// that property is absent or is not a plain string (a union, or a nested schema definition).
    /// </summary>
    private static string? GetKind(JObject obj)
        => obj["type"] is JValue { Type: JTokenType.String } typeValue ? typeValue.Value<string>() : null;

    private static void Walk(
        JToken? node,
        IReadOnlyDictionary<string, JObject> namedSchemas,
        HashSet<string> visited,
        List<JObject> results,
        string? namespaceContext)
    {
        switch (node)
        {
            case JValue { Type: JTokenType.String } value:
            {
                var name = value.Value<string>();
                if (!string.IsNullOrEmpty(name) && TryResolveNamedSchema(name, namespaceContext, namedSchemas, out _, out var referenced))
                    Walk(referenced, namedSchemas, visited, results, namespaceContext);
                return;
            }

            case JArray union:
                foreach (var branch in union)
                    Walk(branch, namedSchemas, visited, results, namespaceContext);
                return;

            case JObject obj:
            {
                var kind = GetKind(obj);
                if (kind != null && NamedSchemaKinds.Contains(kind))
                {
                    var fullName = GetFullName(obj);
                    if (!visited.Add(fullName))
                        return;

                    results.Add(obj);

                    if (kind is "record" or "error" && obj["fields"] is JArray fields)
                    {
                        var nestedNamespace = GetNamespace(fullName);
                        foreach (var field in fields.OfType<JObject>())
                            Walk(field["type"], namedSchemas, visited, results, nestedNamespace);
                    }

                    return;
                }

                if (obj["items"] is { } items)
                    Walk(items, namedSchemas, visited, results, namespaceContext);

                if (obj["values"] is { } values)
                    Walk(values, namedSchemas, visited, results, namespaceContext);

                if (obj["type"] is { } wrapped)
                    Walk(wrapped, namedSchemas, visited, results, namespaceContext);

                return;
            }
        }
    }

    /// <summary>
    /// Produces a self-contained copy of <paramref name="namedSchema"/>: every bare reference it
    /// reaches to another named type is replaced with that type's full definition (recursing through
    /// it in turn), except one that points back to a type already expanded higher up, including
    /// <paramref name="namedSchema"/> itself for a recursive type, which is left as a bare name. This
    /// mirrors how the Avro object model serialises a single named type in isolation.
    /// </summary>
    public static JObject Inline(JObject namedSchema, IReadOnlyDictionary<string, JObject> namedSchemas)
    {
        ArgumentNullException.ThrowIfNull(namedSchema);
        ArgumentNullException.ThrowIfNull(namedSchemas);

        var fullName = GetFullName(namedSchema);
        var written = new HashSet<string>(StringComparer.Ordinal) { fullName };
        var clone = (JObject)namedSchema.DeepClone();
        InlineChildren(clone, namedSchemas, written, GetNamespace(fullName));
        return clone;
    }

    private static void InlineChildren(
        JObject obj,
        IReadOnlyDictionary<string, JObject> namedSchemas,
        HashSet<string> written,
        string? namespaceContext)
    {
        if (obj["fields"] is JArray fields)
        {
            foreach (var field in fields.OfType<JObject>())
            {
                if (field["type"] is { } fieldType)
                    field["type"] = InlineTypeRef(fieldType, namedSchemas, written, namespaceContext);
            }
        }

        if (obj["items"] is { } items)
            obj["items"] = InlineTypeRef(items, namedSchemas, written, namespaceContext);

        if (obj["values"] is { } values)
            obj["values"] = InlineTypeRef(values, namedSchemas, written, namespaceContext);

        if (obj["type"] is { } wrapped)
            obj["type"] = InlineTypeRef(wrapped, namedSchemas, written, namespaceContext);
    }

    private static JToken InlineTypeRef(
        JToken node,
        IReadOnlyDictionary<string, JObject> namedSchemas,
        HashSet<string> written,
        string? namespaceContext)
    {
        switch (node)
        {
            case JValue { Type: JTokenType.String } value:
            {
                var name = value.Value<string>();
                if (string.IsNullOrEmpty(name)
                    || !TryResolveNamedSchema(name, namespaceContext, namedSchemas, out var fullName, out var target)
                    || !written.Add(fullName))
                {
                    return node;
                }

                var clone = (JObject)target.DeepClone();
                InlineChildren(clone, namedSchemas, written, GetNamespace(fullName));
                return clone;
            }

            case JArray union:
            {
                var branches = new JArray();
                foreach (var branch in union)
                    branches.Add(InlineTypeRef(branch, namedSchemas, written, namespaceContext));
                return branches;
            }

            case JObject obj:
                InlineChildren(obj, namedSchemas, written, namespaceContext);
                return obj;

            default:
                return node;
        }
    }
}
