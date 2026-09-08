using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// Checks the default value written against a declaration for one the declared type cannot hold,
/// such as an integer too large for an <c>int</c> or a value that does not match the first branch
/// of a union, which is the branch a union takes its default from.
/// </summary>
/// <remarks>
/// A named type that cannot be resolved is accepted rather than rejected: a type declared later in
/// the document is not yet known while the declarations ahead of it are being read, and refusing a
/// default there would reject a document that is perfectly valid.
/// </remarks>
internal static class IdlDefaultValue
{
    // a record whose fields default back to the record itself can be descended forever, so the
    // check gives up further down than any document nests rather than following it
    private const int MaxDepth = 64;

    // long enough to recognise the offending value in a message, short enough not to repeat a
    // whole document back at the reader
    private const int MaxDescriptionLength = 60;

    /// <summary>
    /// Describes why a default value cannot be held by the type it was written against.
    /// </summary>
    /// <param name="type">The translated type of the declaration the default was written against.</param>
    /// <param name="value">The translated default value.</param>
    /// <param name="resolveNamedType">Resolves the definition of a type referred to by name, returning <c>null</c> when no such type is known.</param>
    /// <returns>The reason the value cannot be held by the type, or <c>null</c> when it can.</returns>
    public static string? DescribeMismatch(JToken type, JToken value, Func<string, JToken?> resolveNamedType)
        => DescribeMismatch(type, value, resolveNamedType, 0);

    private static string? DescribeMismatch(JToken type, JToken value, Func<string, JToken?> resolveNamedType, int depth)
    {
        if (depth > MaxDepth)
            return null;

        return type switch
        {
            JArray union => DescribeUnionMismatch(union, value, resolveNamedType, depth),
            JObject obj => DescribeObjectMismatch(obj, value, resolveNamedType, depth),
            JValue { Type: JTokenType.String } name => DescribeNamedMismatch(name.Value<string>()!, value, resolveNamedType, depth),
            _ => null
        };
    }

    /// <summary>
    /// A union takes its default from its first branch, so a value matching any other branch is
    /// still not a value the union can default to.
    /// </summary>
    private static string? DescribeUnionMismatch(JArray union, JToken value, Func<string, JToken?> resolveNamedType, int depth)
    {
        if (union.Count == 0)
            return null;

        var reason = DescribeMismatch(union[0], value, resolveNamedType, depth + 1);

        return reason == null
            ? null
            : $"a union takes its default from its first branch, and {reason}";
    }

    private static string? DescribeObjectMismatch(JObject type, JToken value, Func<string, JToken?> resolveNamedType, int depth)
    {
        if (!type.TryGetValue("type", out var typeToken))
            return null;

        // a type wrapped so that annotations can be attached to it, e.g. { "type": ["null", "int"] }
        if (typeToken is not JValue { Type: JTokenType.String } typeName)
            return DescribeMismatch(typeToken, value, resolveNamedType, depth + 1);

        return typeName.Value<string>() switch
        {
            "record" or "error" => DescribeRecordMismatch(type, value, resolveNamedType, depth),
            "array" => DescribeArrayMismatch(type, value, resolveNamedType, depth),
            "map" => DescribeMapMismatch(type, value, resolveNamedType, depth),
            "enum" => DescribeTextualMismatch(type, value, "enum"),
            "fixed" => DescribeTextualMismatch(type, value, "fixed"),
            var name => DescribeNamedMismatch(name!, value, resolveNamedType, depth)
        };
    }

    private static string? DescribeNamedMismatch(string typeName, JToken value, Func<string, JToken?> resolveNamedType, int depth)
    {
        switch (typeName)
        {
            case "null":
                return value.Type == JTokenType.Null ? null : NotValid(typeName, value);
            case "boolean":
                return value.Type == JTokenType.Boolean ? null : NotValid(typeName, value);
            case "int":
                if (!TryGetInteger(value, out var intValue))
                    return NotValid(typeName, value);
                return intValue is >= int.MinValue and <= int.MaxValue
                    ? null
                    : $"the value {Describe(value)} is outside the range of \"int\"";
            case "long":
                return TryGetInteger(value, out _)
                    ? null
                    : value.Type == JTokenType.Integer
                        ? $"the value {Describe(value)} is outside the range of \"long\""
                        : NotValid(typeName, value);
            case "float" or "double":
                return value.Type is JTokenType.Integer or JTokenType.Float ? null : NotValid(typeName, value);
            case "bytes" or "string":
                return value.Type == JTokenType.String ? null : NotValid(typeName, value);
            default:
                var resolved = resolveNamedType(typeName);
                return resolved == null
                    ? null
                    : DescribeMismatch(resolved, value, resolveNamedType, depth + 1);
        }
    }

    private static string? DescribeRecordMismatch(JObject record, JToken value, Func<string, JToken?> resolveNamedType, int depth)
    {
        var recordName = record["name"]?.Value<string>() ?? "record";

        if (value is not JObject given)
            return $"the value {Describe(value)} is not a JSON object, which a '{recordName}' value must be";

        if (record["fields"] is not JArray fields)
            return null;

        foreach (var field in fields.OfType<JObject>())
        {
            var fieldName = field["name"]?.Value<string>();
            var fieldType = field["type"];
            if (fieldName == null || fieldType == null)
                continue;

            // a field the value says nothing about takes the default from its own declaration,
            // so a field that has neither leaves the record's default incomplete
            var fieldValue = given.TryGetValue(fieldName, out var writtenValue)
                ? writtenValue
                : field["default"];

            if (fieldValue == null)
                return $"no value was given for field '{fieldName}' of '{recordName}', which has no default value of its own";

            var reason = DescribeMismatch(fieldType, fieldValue, resolveNamedType, depth + 1);
            if (reason != null)
                return $"for field '{fieldName}' of '{recordName}', {reason}";
        }

        return null;
    }

    private static string? DescribeArrayMismatch(JObject array, JToken value, Func<string, JToken?> resolveNamedType, int depth)
    {
        if (value is not JArray elements)
            return $"the value {Describe(value)} is not a JSON array, which an \"array\" value must be";

        var itemType = array["items"];
        if (itemType == null)
            return null;

        foreach (var element in elements)
        {
            var reason = DescribeMismatch(itemType, element, resolveNamedType, depth + 1);
            if (reason != null)
                return $"for an element of the array, {reason}";
        }

        return null;
    }

    private static string? DescribeMapMismatch(JObject map, JToken value, Func<string, JToken?> resolveNamedType, int depth)
    {
        if (value is not JObject entries)
            return $"the value {Describe(value)} is not a JSON object, which a \"map\" value must be";

        var valueType = map["values"];
        if (valueType == null)
            return null;

        foreach (var entry in entries.Properties())
        {
            var reason = DescribeMismatch(valueType, entry.Value, resolveNamedType, depth + 1);
            if (reason != null)
                return $"for map entry '{entry.Name}', {reason}";
        }

        return null;
    }

    private static string? DescribeTextualMismatch(JObject type, JToken value, string kind)
    {
        if (value.Type == JTokenType.String)
            return null;

        var name = type["name"]?.Value<string>() ?? kind;

        return $"the value {Describe(value)} is not a string, which a '{name}' value must be";
    }

    private static string NotValid(string typeName, JToken value)
        => $"the value {Describe(value)} is not a valid \"{typeName}\" value";

    /// <summary>
    /// Whether a value is a whole number that a 64-bit integer can hold. A literal too large for
    /// one is read as a <see cref="System.Numerics.BigInteger"/>, which is a whole number all the
    /// same, so the two cases are told apart by the caller.
    /// </summary>
    private static bool TryGetInteger(JToken value, out long result)
    {
        result = 0;

        if (value.Type != JTokenType.Integer)
            return false;

        switch ((value as JValue)?.Value)
        {
            case long longValue:
                result = longValue;
                return true;
            case int intValue:
                result = intValue;
                return true;
            default:
                return false;
        }
    }

    private static string Describe(JToken value)
    {
        var text = value.ToString(Formatting.None);

        return text.Length > MaxDescriptionLength
            ? string.Concat(text.AsSpan(0, MaxDescriptionLength), "...")
            : text;
    }
}
