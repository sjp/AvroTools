using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avro;
using Avro.Util;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace SJP.Avro.Tools.CodeGen;

internal static class AvroSchemaUtilities
{
    /// <summary>
    /// The name generated code refers to <see cref="AvroDecimal"/> by. Every library type is named
    /// in full, so that a schema is free to declare a type or a field of the same name.
    /// </summary>
    public static readonly NameSyntax AvroDecimalType = SyntaxUtilities.GlobalName(typeof(AvroDecimal));

    /// <summary>
    /// The name generated code refers to <see cref="Schema"/> by.
    /// </summary>
    public static readonly NameSyntax AvroSchemaType = SyntaxUtilities.GlobalName(typeof(Schema));

    /// <summary>
    /// The name generated code refers to <see cref="Protocol"/> by.
    /// </summary>
    public static readonly NameSyntax AvroProtocolType = SyntaxUtilities.GlobalName(typeof(Protocol));

    /// <summary>
    /// The name of the property that hands out the schema a generated record, error or fixed type
    /// was built from. It is fixed by <c>ISpecificRecord</c> and by the Avro base types.
    /// </summary>
    public const string SchemaMemberName = "Schema";

    /// <summary>
    /// The name of the property that hands out the protocol a generated protocol type was built
    /// from. It is fixed by <c>ISpecificProtocol</c>.
    /// </summary>
    public const string ProtocolMemberName = "Protocol";

    /// <summary>
    /// The name of the field holding the parsed schema, before anything else in the generated type
    /// lays claim to it.
    /// </summary>
    public const string SchemaFieldName = "_schema";

    /// <summary>
    /// The name of the field holding the parsed protocol, before anything else in the generated
    /// type lays claim to it.
    /// </summary>
    public const string ProtocolFieldName = "_protocol";

    /// <summary>
    /// Determines the C# type a record field, message parameter or message response is generated as.
    /// </summary>
    /// <param name="schema">The schema of the position being typed.</param>
    /// <param name="containingNamespace">
    /// The namespace the generated type that holds this position is declared in, used to place a
    /// referenced Avro type that declares no namespace of its own.
    /// </param>
    /// <returns>The type syntax for the position.</returns>
    /// <exception cref="NotSupportedException">
    /// <paramref name="schema"/> holds a logical type the code generator cannot express as a C#
    /// type, either because Avro implements it in a form the specific API rejects or because the
    /// generator has no mapping for it.
    /// </exception>
    public static TypeSyntax GetFieldType(Schema schema, string containingNamespace)
    {
        return GetFieldType(schema, containingNamespace, convertDecimals: true);
    }

    /// <summary>
    /// Determines the C# type a position that exchanges values with Avro exactly as the runtime
    /// represents them is generated as. It differs from <see cref="GetFieldType(Schema, string)"/> only in a
    /// decimal, which stays an <see cref="AvroDecimal"/> rather than being converted: a protocol
    /// message hands its parameters and its response straight to and from the requestor, with no
    /// generated body in between to convert them.
    /// </summary>
    /// <param name="schema">The schema of the position being typed.</param>
    /// <param name="containingNamespace">
    /// The namespace the generated type that holds this position is declared in, used to place a
    /// referenced Avro type that declares no namespace of its own.
    /// </param>
    /// <returns>The type syntax for the position.</returns>
    /// <exception cref="NotSupportedException">
    /// <paramref name="schema"/> holds a logical type the code generator cannot express as a C#
    /// type, either because Avro implements it in a form the specific API rejects or because the
    /// generator has no mapping for it.
    /// </exception>
    public static TypeSyntax GetRuntimeFieldType(Schema schema, string containingNamespace)
    {
        return GetFieldType(schema, containingNamespace, convertDecimals: false);
    }

    private static TypeSyntax GetFieldType(Schema schema, string containingNamespace, bool convertDecimals)
    {
        var fieldType = GetSimpleFieldType(schema, containingNamespace, convertDecimals);
        var fieldIsNullable = IsNullable(schema);
        return fieldIsNullable ? NullableType(fieldType) : fieldType;
    }

    private static TypeSyntax GetSimpleFieldType(Schema schema, string containingNamespace, bool convertDecimals)
    {
        if (SyntaxUtilities.TypeSyntaxMap.TryGetValue(schema.Tag, out var builtinType))
            return builtinType;

        if (schema is LogicalSchema logicalSchema)
            return ResolveLogicalType(logicalSchema, containingNamespace, convertDecimals);

        if (schema is ArraySchema arraySchema)
            return ResolveArrayType(arraySchema, containingNamespace);

        if (schema is MapSchema mapSchema)
            return ResolveMapType(mapSchema, containingNamespace);

        if (schema is UnionSchema unionSchema)
            return ResolveUnionType(unionSchema, containingNamespace, convertDecimals);

        return ResolveNamedType((NamedSchema)schema, containingNamespace);
    }

    /// <summary>
    /// Names a type generated from another Avro schema. The name is written out in full so that it
    /// binds to that type and no other: an Avro schema may declare two types of the same name in
    /// different namespaces, or a type whose name is one the generated code already uses.
    /// </summary>
    private static TypeSyntax ResolveNamedType(NamedSchema schema, string containingNamespace)
    {
        var ns = SyntaxUtilities.ResolveNamespace(schema.Namespace, containingNamespace, schema.Fullname);
        return SyntaxUtilities.GlobalName(ns, SyntaxUtilities.SafeIdentifierName(schema.Name));
    }

    private static TypeSyntax ResolveLogicalType(LogicalSchema logicalSchema, string containingNamespace, bool convertDecimals)
    {
        // A logical type Avro does not implement is carried through untouched: values are handed
        // to and from the generated code as the type that backs the logical type, so that is what
        // the member has to be typed as.
        if (logicalSchema.LogicalType is UnknownLogicalType)
            return GetSimpleFieldType(logicalSchema.BaseSchema, containingNamespace, convertDecimals);

        return logicalSchema.LogicalTypeName switch
        {
            DecimalLogicalTypeName => ResolveDecimalType(logicalSchema, convertDecimals),
            "date" => SyntaxUtilities.GlobalName(typeof(DateTime)),
            "time-millis" => SyntaxUtilities.GlobalName(typeof(TimeSpan)),
            "time-micros" => SyntaxUtilities.GlobalName(typeof(TimeSpan)),
            "timestamp-millis" => SyntaxUtilities.GlobalName(typeof(DateTime)),
            "timestamp-micros" => SyntaxUtilities.GlobalName(typeof(DateTime)),
            "local-timestamp-millis" => SyntaxUtilities.GlobalName(typeof(DateTime)),
            "local-timestamp-micros" => SyntaxUtilities.GlobalName(typeof(DateTime)),
            "uuid" => SyntaxUtilities.GlobalName(typeof(Guid)),
            _ => throw new NotSupportedException(
                $"The logical type '{logicalSchema.LogicalTypeName}' is not supported by the code generator.")
        };
    }

    private static TypeSyntax ResolveDecimalType(LogicalSchema decimalSchema, bool convertDecimals)
    {
        // Avro turns a decimal stored in a 'fixed' into a generic fixed on the way out and hands one
        // back on the way in, and its specific writer and reader accept neither, so no member type
        // can carry such a value across the ISpecificRecord boundary. Refusing the schema is better
        // than emitting code that throws the first time it is written or read.
        if (decimalSchema.BaseSchema is FixedSchema backingFixed)
        {
            throw new NotSupportedException(
                $"The decimal backed by the fixed type '{backingFixed.Fullname}' cannot be generated. "
                + "Apache.Avro exchanges a fixed-backed decimal as a generic fixed, which its specific "
                + "writer and reader both reject. Store the decimal in 'bytes' instead.");
        }

        return convertDecimals && IsRepresentableAsDecimal(decimalSchema)
            ? PredefinedType(Token(SyntaxKind.DecimalKeyword))
            : AvroDecimalType;
    }

    private static TypeSyntax ResolveArrayType(ArraySchema arraySchema, string containingNamespace)
    {
        // Values nested inside a collection are handed to and from Avro element by element, so
        // they keep the representation the runtime uses rather than a converted one.
        var value = GetFieldType(arraySchema.ItemSchema, containingNamespace, convertDecimals: false);

        // An array is typed by its interface, not by List<T>. Avro builds the container for an
        // array nested inside another array, a map or a union as a List<IList<T>> or a
        // Dictionary<string, IList<T>>, and generic collections are invariant, so a member typed
        // List<List<T>> could not be cast to or from what the runtime actually hands over.
        return SyntaxUtilities.GlobalGenericName(typeof(IList<>), value);
    }

    private static TypeSyntax ResolveMapType(MapSchema mapSchema, string containingNamespace)
    {
        var value = GetFieldType(mapSchema.ValueSchema, containingNamespace, convertDecimals: false);
        return SyntaxUtilities.GlobalGenericName(
            typeof(IDictionary<,>),
            PredefinedType(Token(SyntaxKind.StringKeyword)),
            value);
    }

    private static TypeSyntax ResolveUnionType(UnionSchema unionSchema, string containingNamespace, bool convertDecimals)
    {
        var nonNullSchemas = unionSchema.Schemas
            .Where(s => s.Tag != Schema.Type.Null)
            .ToList();

        // A union only maps onto a single C# type when exactly one branch remains once null is
        // ignored. Two branches could each be handed to Put at runtime, and two records or two
        // enums are as distinct as a record and a string, so anything else falls back to 'object'.
        // A union of nothing but null has no type to fall back to either.
        if (nonNullSchemas.Count != 1)
            return PredefinedType(Token(SyntaxKind.ObjectKeyword));

        return GetFieldType(nonNullSchemas[0], containingNamespace, convertDecimals);
    }

    /// <summary>
    /// The name Avro gives the logical type whose values are exchanged as <see cref="AvroDecimal"/>.
    /// </summary>
    public const string DecimalLogicalTypeName = "decimal";

    /// <summary>
    /// Returns the decimal schema behind a position whose generated type is <c>decimal</c>, meaning
    /// values must be converted to and from <see cref="AvroDecimal"/> when crossing the
    /// <c>ISpecificRecord</c> boundary. A decimal qualifies when it is the field's own type or the
    /// only non-null branch of its union. Every other position, including a decimal nested in an
    /// array or a map, needs no conversion and yields <c>null</c>.
    /// </summary>
    /// <param name="schema">The schema of a record field.</param>
    /// <returns>The decimal schema to convert, or <c>null</c> when no conversion applies.</returns>
    public static LogicalSchema? GetConvertedDecimalSchema(Schema schema)
    {
        if (schema is LogicalSchema { LogicalTypeName: DecimalLogicalTypeName } decimalSchema)
            return IsRepresentableAsDecimal(decimalSchema) ? decimalSchema : null;

        if (schema is not UnionSchema unionSchema)
            return null;

        var nonNullSchemas = unionSchema.Schemas
            .Where(s => s.Tag != Schema.Type.Null)
            .ToList();

        return nonNullSchemas.Count == 1
            && nonNullSchemas[0] is LogicalSchema { LogicalTypeName: DecimalLogicalTypeName } branchSchema
            && IsRepresentableAsDecimal(branchSchema)
                ? branchSchema
                : null;
    }

    /// <summary>
    /// The largest scale a C# <c>decimal</c> can hold. Avro puts no such limit on a decimal: it
    /// admits any scale up to the type's precision.
    /// </summary>
    private const int MaxDecimalScale = 28;

    /// <summary>
    /// Determines whether a decimal schema's values fit the C# <c>decimal</c> type at all. A scale
    /// beyond what <c>decimal</c> can hold leaves no value to convert, so such a position keeps
    /// Avro's own <see cref="AvroDecimal"/> representation instead.
    /// </summary>
    /// <param name="decimalSchema">A schema whose logical type is <c>decimal</c>.</param>
    /// <returns><c>true</c> if the schema's values can be carried by a C# <c>decimal</c>.</returns>
    private static bool IsRepresentableAsDecimal(LogicalSchema decimalSchema)
    {
        return GetDecimalScale(decimalSchema) <= MaxDecimalScale;
    }

    /// <summary>
    /// Reads the scale of a decimal schema. Avro makes <c>scale</c> optional, defaulting to zero.
    /// </summary>
    /// <param name="decimalSchema">A schema whose logical type is <c>decimal</c>.</param>
    /// <returns>The number of digits to the right of the decimal point.</returns>
    public static int GetDecimalScale(LogicalSchema decimalSchema)
    {
        var scale = decimalSchema.GetProperty("scale");
        return scale == null
            ? 0
            : int.Parse(scale, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Determines whether a schema position also admits a null value, i.e. whether its generated
    /// type carries a <c>?</c> annotation. Only a union can, and it does so whenever one of its
    /// branches is null, whatever the remaining branches map onto.
    /// </summary>
    /// <param name="schema">The schema of a record field.</param>
    /// <returns><c>true</c> if the position is nullable, otherwise <c>false</c>.</returns>
    public static bool IsNullable(Schema schema)
    {
        return schema is UnionSchema unionSchema
            && unionSchema.Schemas.Any(s => s.Tag == Schema.Type.Null);
    }

    public static bool IsValueType(Schema schema)
    {
        if (ValueTypes.Contains(schema.Tag))
            return true;

        if (schema is LogicalSchema logicalSchema)
        {
            return logicalSchema.LogicalType is UnknownLogicalType
                ? IsValueType(logicalSchema.BaseSchema)
                : ValueTypeLogicalTypeNames.Contains(logicalSchema.LogicalTypeName);
        }

        return false;
    }

    private static readonly FrozenSet<string> ValueTypeLogicalTypeNames = FrozenSet.Create(
        StringComparer.Ordinal,
        DecimalLogicalTypeName,
        "date",
        "time-millis",
        "time-micros",
        "timestamp-millis",
        "timestamp-micros",
        "local-timestamp-millis",
        "local-timestamp-micros",
        "uuid");

    private static readonly FrozenSet<Schema.Type> ValueTypes = FrozenSet.Create(
        Schema.Type.Boolean,
        Schema.Type.Int,
        Schema.Type.Long,
        Schema.Type.Float,
        Schema.Type.Double,
        Schema.Type.Enumeration);

    /// <summary>
    /// Rewrites the JSON of a schema or protocol into the shape the Avro specification defines for
    /// a logical type. <c>Apache.Avro</c> writes a logical type backed by a named type as a wrapper
    /// around it — <c>{ "type": { "type": "fixed", ... }, "logicalType": "duration" }</c> — which
    /// most Avro implementations cannot parse, while the specification puts the logical attributes
    /// on the type itself. JSON containing no such wrapper is returned exactly as it was given.
    /// </summary>
    /// <param name="json">The JSON text of a schema or a protocol.</param>
    /// <returns>The same document with every logical type written in its portable form.</returns>
    public static string ToPortableJson(string json)
    {
        var flattened = false;
        var rewritten = FlattenLogicalTypes(JsonNode.Parse(json), ref flattened);

        return flattened
            ? rewritten!.ToJsonString(PortableJsonOptions)
            : json;
    }

    private static JsonNode? FlattenLogicalTypes(JsonNode? node, ref bool flattened)
    {
        if (node is JsonArray array)
        {
            var branches = new JsonArray();
            foreach (var branch in array)
                branches.Add(FlattenLogicalTypes(branch, ref flattened));

            return branches;
        }

        if (node is not JsonObject obj)
            return node?.DeepClone();

        var rewritten = new JsonObject();
        foreach (var (name, value) in obj)
            rewritten[name] = FlattenLogicalTypes(value, ref flattened);

        // A wrapper holds the logical attributes and the type they apply to, nothing else. A record
        // field also pairs a 'type' with further attributes, but it is always named, so a custom
        // 'logicalType' attribute on a field is never mistaken for a wrapper.
        if (rewritten["logicalType"] is not JsonValue
            || rewritten["type"] is not JsonObject baseType
            || rewritten.ContainsKey("name"))
        {
            return rewritten;
        }

        flattened = true;

        var merged = (JsonObject)baseType.DeepClone();
        foreach (var (name, value) in rewritten)
        {
            if (!string.Equals(name, "type", StringComparison.Ordinal))
                merged[name] = value?.DeepClone();
        }

        return merged;
    }

    /// <summary>
    /// Matches how <c>Apache.Avro</c> writes a schema: no indentation, and no escaping beyond what
    /// JSON itself requires, so a name or a doc comment outside ASCII stays readable.
    /// </summary>
    private static readonly JsonSerializerOptions PortableJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static FieldDeclarationSyntax CreateProtocolDefinition(string json, string fieldName)
    {
        return FieldDeclaration(
            VariableDeclaration(
                AvroProtocolType)
            .WithVariables(
                SingletonSeparatedList(
                    VariableDeclarator(
                        Identifier(fieldName))
                    .WithInitializer(
                        EqualsValueClause(
                            InvocationExpression(
                                MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    AvroProtocolType,
                                    IdentifierName(nameof(Protocol.Parse))))
                            .WithArgumentList(
                                ArgumentList(
                                    SingletonSeparatedList(
                                        Argument(
                                            LiteralExpression(
                                                SyntaxKind.StringLiteralExpression,
                                                Literal(json)))))))))))
            .WithModifiers(
                    TokenList(
                        Token(SyntaxKind.PrivateKeyword),
                        Token(SyntaxKind.StaticKeyword),
                        Token(SyntaxKind.ReadOnlyKeyword)));
    }

    public static PropertyDeclarationSyntax CreateProtocolProperty(string fieldName)
    {
        return PropertyDeclaration(
                AvroProtocolType,
                Identifier(ProtocolMemberName))
            .WithModifiers(
                TokenList(
                    Token(SyntaxKind.PublicKeyword)))
            .WithAccessorList(
                AccessorList(
                    SingletonList(
                        AccessorDeclaration(
                            SyntaxKind.GetAccessorDeclaration)
                        .WithSemicolonToken(
                            Token(SyntaxKind.SemicolonToken)))))
            .WithInitializer(
                EqualsValueClause(
                    IdentifierName(fieldName)))
            .WithSemicolonToken(
                Token(SyntaxKind.SemicolonToken))
            .WithTrailingTrivia(TriviaList(CarriageReturnLineFeed, CarriageReturnLineFeed));
    }

    public static FieldDeclarationSyntax CreateSchemaDefinition(string json, string fieldName)
    {
        return FieldDeclaration(
            VariableDeclaration(
                AvroSchemaType)
            .WithVariables(
                SingletonSeparatedList(
                    VariableDeclarator(
                        Identifier(fieldName))
                    .WithInitializer(
                        EqualsValueClause(
                            InvocationExpression(
                                MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    AvroSchemaType,
                                    IdentifierName(nameof(Schema.Parse))))
                            .WithArgumentList(
                                ArgumentList(
                                    SingletonSeparatedList(
                                        Argument(
                                            LiteralExpression(
                                                SyntaxKind.StringLiteralExpression,
                                                Literal(json)))))))))))
            .WithModifiers(
                    TokenList(
                        Token(SyntaxKind.PrivateKeyword),
                        Token(SyntaxKind.StaticKeyword),
                        Token(SyntaxKind.ReadOnlyKeyword)));
    }

    public static PropertyDeclarationSyntax CreateSchemaProperty(string fieldName)
    {
        return PropertyDeclaration(
                AvroSchemaType,
                Identifier(SchemaMemberName))
            .WithModifiers(
                TokenList(
                    Token(SyntaxKind.PublicKeyword)))
            .WithAccessorList(
                AccessorList(
                    SingletonList(
                        AccessorDeclaration(
                            SyntaxKind.GetAccessorDeclaration)
                        .WithSemicolonToken(
                            Token(SyntaxKind.SemicolonToken)))))
            .WithInitializer(
                EqualsValueClause(
                    IdentifierName(fieldName)))
            .WithSemicolonToken(
                Token(SyntaxKind.SemicolonToken))
            .WithTrailingTrivia(TriviaList(CarriageReturnLineFeed, CarriageReturnLineFeed));
    }
}