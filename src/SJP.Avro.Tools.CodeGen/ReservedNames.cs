using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace SJP.Avro.Tools.CodeGen;

/// <summary>
/// Names a generated member may not take. An Avro name may be anything at all, so a field, a
/// message or a type can arrive named after something C# or the base type the generated code
/// derives from has already spoken for. Declaring one anyway either fails to compile or hides an
/// inherited member, which is fatal in a project that treats warnings as errors.
/// </summary>
internal static class ReservedNames
{
    /// <summary>
    /// Members every type inherits from <see cref="object"/>. Declaring one hides the inherited
    /// member, and hiding one of these is never what a schema meant.
    /// </summary>
    public static readonly IReadOnlySet<string> ObjectMembers = new HashSet<string>(StringComparer.Ordinal)
    {
        "Equals",
        "Finalize",
        "GetHashCode",
        "GetType",
        "MemberwiseClone",
        "ReferenceEquals",
        "ToString"
    }.ToFrozenSet();

    /// <summary>
    /// Members the compiler writes into every C# <c>record</c>. A member of the same name is either
    /// a duplicate definition, the wrong shape for the synthesised member, or, in the case of
    /// <c>Clone</c>, disallowed in a record outright.
    /// </summary>
    public static readonly IReadOnlySet<string> RecordMembers = new HashSet<string>(StringComparer.Ordinal)
    {
        "Clone",
        "EqualityContract",
        "PrintMembers"
    }.ToFrozenSet();

    /// <summary>
    /// Members inherited from <see cref="Exception"/>, which a generated error type derives from.
    /// Several of them — <c>Data</c>, <c>Message</c>, <c>Source</c> — are ordinary Avro field names.
    /// </summary>
    public static readonly IReadOnlySet<string> ExceptionMembers = new HashSet<string>(StringComparer.Ordinal)
    {
        "Data",
        "GetBaseException",
        "GetObjectData",
        "HelpLink",
        "HResult",
        "InnerException",
        "Message",
        "Source",
        "StackTrace",
        "TargetSite"
    }.ToFrozenSet();

    /// <summary>
    /// Returns <paramref name="name"/>, suffixed with as many underscores as it takes to tell it
    /// apart from every name already claimed.
    /// </summary>
    /// <param name="name">The name that would be used if nothing else claimed it.</param>
    /// <param name="taken">The names already claimed.</param>
    /// <returns>A name no other member uses.</returns>
    public static string MakeAvailable(string name, IReadOnlySet<string> taken)
    {
        var candidate = name;
        while (taken.Contains(candidate))
            candidate += "_";

        return candidate;
    }
}
