using System;
using System.Collections.Generic;
using Avro;

namespace SJP.Avro.Tools.CodeGen;

/// <summary>
/// Checks that the names an Avro definition declares can be carried over into C#. Generated code
/// spells every declared name exactly as the definition does, so a name C# has no way to write
/// produces source that does not compile.
/// </summary>
/// <remarks>
/// <c>Apache.Avro</c> already refuses a type, field, message or enum symbol name that is not a
/// legal Avro name, and a legal Avro name is always a legal C# identifier. It places no such
/// restriction on a namespace or on a protocol's own name, which is what is checked here.
/// </remarks>
public static class AvroNameValidation
{
    /// <summary>
    /// Finds the names a protocol declares that C# cannot express.
    /// </summary>
    /// <param name="protocol">A definition of an Avro protocol.</param>
    /// <returns>The offending names, in declaration order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="protocol"/> is <c>null</c>.</exception>
    public static IReadOnlyList<string> FindUnusableNames(Protocol protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);

        var unusable = new List<string>();

        AddUnusableNamespace(unusable, protocol.Namespace);

        if (!CsharpValidation.IsValidCsharpIdentifier(protocol.Name))
            unusable.Add(protocol.Name);

        return unusable;
    }

    /// <summary>
    /// Finds the names a record, error, enum or fixed type declares that C# cannot express. Only
    /// the names this type declares are considered; a type it refers to is generated in its own
    /// right and is checked separately.
    /// </summary>
    /// <param name="schema">A definition of a named Avro type.</param>
    /// <returns>The offending names, in declaration order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="schema"/> is <c>null</c>.</exception>
    public static IReadOnlyList<string> FindUnusableNames(NamedSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var unusable = new List<string>();

        AddUnusableNamespace(unusable, schema.Namespace);

        return unusable;
    }

    private static void AddUnusableNamespace(List<string> unusable, string? declaredNamespace)
    {
        // A type or protocol that declares no namespace takes the base namespace instead, which is
        // validated where it is supplied.
        if (string.IsNullOrWhiteSpace(declaredNamespace))
            return;

        foreach (var segment in declaredNamespace.Split('.'))
        {
            if (!CsharpValidation.IsValidCsharpIdentifier(segment))
            {
                unusable.Add(declaredNamespace);
                return;
            }
        }
    }
}
