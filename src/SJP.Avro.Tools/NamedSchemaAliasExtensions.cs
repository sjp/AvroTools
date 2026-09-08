using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avro;

namespace SJP.Avro.Tools;

/// <summary>
/// Alias lookup for Avro named types, i.e. records, errors, enums and fixed types.
/// </summary>
/// <remarks>
/// Apache.Avro keeps a named type's aliases to itself: the list is private and the matching that
/// reads it is protected. Both are bound directly here rather than reimplemented, so alias handling
/// stays whatever Apache.Avro does. A binding fails on first use, naming the member it could not
/// find, if either is ever renamed or removed; probing for them instead would leave every type
/// looking as though it had no aliases at all.
/// </remarks>
internal static class NamedSchemaAliasExtensions
{
    /// <summary>
    /// Determines whether <paramref name="schema"/> lists <paramref name="other"/>'s name among its
    /// aliases, i.e. whether <paramref name="other"/> names the same type under an earlier name.
    /// </summary>
    /// <param name="schema">The schema whose aliases are searched.</param>
    /// <param name="other">The schema whose name is looked for.</param>
    /// <returns><c>true</c> if <paramref name="other"/>'s name is one of <paramref name="schema"/>'s aliases.</returns>
    public static bool HasAliasFor(this NamedSchema schema, NamedSchema other) =>
        InAliases(schema, other.SchemaName);

    /// <summary>
    /// The full names of <paramref name="schema"/>'s aliases, empty when it declares none. A bare
    /// alias is qualified with the namespace it was declared in, matching how the name it stands in
    /// for is resolved.
    /// </summary>
    /// <param name="schema">The schema to read the aliases of.</param>
    /// <returns>The alias full names, in declaration order.</returns>
    public static IEnumerable<string> AliasFullnames(this NamedSchema schema) =>
        Aliases(schema)?.Select(static a => a.Fullname) ?? [];

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "InAliases")]
    private static extern bool InAliases(NamedSchema schema, SchemaName name);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "aliases")]
    private static extern ref IList<SchemaName>? Aliases(NamedSchema schema);
}
