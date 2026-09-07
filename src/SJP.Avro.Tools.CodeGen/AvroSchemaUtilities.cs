using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avro;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace SJP.Avro.Tools.CodeGen;

internal static class AvroSchemaUtilities
{
    public static TypeSyntax GetFieldType(Schema schema)
    {
        return GetFieldType(schema, convertDecimals: true);
    }

    private static TypeSyntax GetFieldType(Schema schema, bool convertDecimals)
    {
        var fieldIsNullable = IsNullableRefType(schema) || IsNullableValueType(schema);
        var fieldType = GetSimpleFieldType(schema, convertDecimals);
        return fieldIsNullable ? NullableType(fieldType) : fieldType;
    }

    public static TypeSyntax GetSimpleFieldType(Schema schema)
    {
        return GetSimpleFieldType(schema, convertDecimals: true);
    }

    private static TypeSyntax GetSimpleFieldType(Schema schema, bool convertDecimals)
    {
        if (SyntaxUtilities.TypeSyntaxMap.TryGetValue(schema.Tag, out var builtinType))
            return builtinType;

        if (schema is LogicalSchema logicalSchema)
            return ResolveLogicalType(logicalSchema, convertDecimals);

        if (schema is ArraySchema arraySchema)
            return ResolveArrayType(arraySchema);

        if (schema is MapSchema mapSchema)
            return ResolveMapType(mapSchema);

        if (schema is UnionSchema unionSchema)
            return ResolveUnionType(unionSchema, convertDecimals);

        return IdentifierName(schema.Name);
    }

    private static TypeSyntax ResolveLogicalType(LogicalSchema logicalSchema, bool convertDecimals)
    {
        return logicalSchema.LogicalTypeName switch
        {
            DecimalLogicalTypeName => convertDecimals
                ? PredefinedType(Token(SyntaxKind.DecimalKeyword))
                : IdentifierName(nameof(AvroDecimal)),
            "date" => IdentifierName(nameof(DateTime)),
            "time-millis" => IdentifierName(nameof(TimeSpan)),
            "time-micros" => IdentifierName(nameof(TimeSpan)),
            "timestamp-millis" => IdentifierName(nameof(DateTime)),
            "timestamp-micros" => IdentifierName(nameof(DateTime)),
            "local-timestamp-millis" => IdentifierName(nameof(DateTime)),
            "local-timestamp-micros" => IdentifierName(nameof(DateTime)),
            "duration" => IdentifierName(nameof(TimeSpan)),
            "uuid" => IdentifierName(nameof(Guid)),
            _ => throw new ArgumentOutOfRangeException($"Unable to resolve a type for logicalType of '{logicalSchema.Name}'")
        };
    }

    private static TypeSyntax ResolveArrayType(ArraySchema arraySchema)
    {
        // Values nested inside a collection are handed to and from Avro element by element, so
        // they keep the representation the runtime uses rather than a converted one.
        var value = GetFieldType(arraySchema.ItemSchema, convertDecimals: false);
        return GenericName(
            Identifier(nameof(System.Collections.Generic.List<>)))
            .WithTypeArgumentList(
                TypeArgumentList(
                    SingletonSeparatedList(value)));
    }

    private static TypeSyntax ResolveMapType(MapSchema mapSchema)
    {
        var value = GetFieldType(mapSchema.ValueSchema, convertDecimals: false);
        return GenericName(
            Identifier(nameof(IDictionary<,>)))
            .WithTypeArgumentList(
                TypeArgumentList(
                    SeparatedList<TypeSyntax>(
                        new SyntaxNodeOrToken[]
                        {
                                PredefinedType(
                                    Token(SyntaxKind.StringKeyword)),
                                Token(SyntaxKind.CommaToken),
                                value
                        })));
    }

    private static TypeSyntax ResolveUnionType(UnionSchema unionSchema, bool convertDecimals)
    {
        var typeCount = unionSchema.Schemas
            .Select(s => s.Tag)
            .Distinct()
            .Count(t => t != Schema.Type.Null);

        // If we have a set of values > 2, we'll need custom code (not possible
        // to automatically generate.
        // Return 'object'.
        if (typeCount > 1)
            return PredefinedType(Token(SyntaxKind.ObjectKeyword));

        var nonNullType = unionSchema.Schemas
            .FirstOrDefault(s => s.Tag != Schema.Type.Null);

        // giving up, only a null value (unable to resolve to anything other than 'object'.
        if (nonNullType == null)
            return PredefinedType(Token(SyntaxKind.ObjectKeyword));

        return GetFieldType(nonNullType, convertDecimals);
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
            return decimalSchema;

        if (schema is not UnionSchema unionSchema)
            return null;

        var nonNullSchemas = unionSchema.Schemas
            .Where(s => s.Tag != Schema.Type.Null)
            .ToList();

        return nonNullSchemas.Count == 1
            && nonNullSchemas[0] is LogicalSchema { LogicalTypeName: DecimalLogicalTypeName } branchSchema
            ? branchSchema
            : null;
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
    /// type carries a <c>?</c> annotation.
    /// </summary>
    /// <param name="schema">The schema of a record field.</param>
    /// <returns><c>true</c> if the position is nullable, otherwise <c>false</c>.</returns>
    public static bool IsNullable(Schema schema)
    {
        return schema is UnionSchema unionSchema
            && unionSchema.Schemas.Any(s => s.Tag == Schema.Type.Null);
    }

    public static bool IsNullableRefType(Schema schema)
    {
        return schema is UnionSchema unionSchema
            && unionSchema.Schemas.Any(s => s.Tag == Schema.Type.Null)
            && !unionSchema.Schemas.Any(s => s.Tag != Schema.Type.Null && IsValueType(schema));
    }

    public static bool IsNullableValueType(Schema schema)
    {
        return schema is UnionSchema unionSchema
            && unionSchema.Schemas.Any(s => s.Tag == Schema.Type.Null)
            && !unionSchema.Schemas.Any(s => s.Tag != Schema.Type.Null && !IsValueType(schema));
    }

    public static bool IsValueType(Schema schema)
    {
        if (ValueTypes.Contains(schema.Tag))
            return true;

        if (schema is LogicalSchema logicalSchema)
            return ValueTypeLogicalTypeNames.Contains(logicalSchema.LogicalTypeName);

        return false;
    }

    private static readonly IEnumerable<string> ValueTypeLogicalTypeNames =
    [
            "decimal",
            "date",
            "time-millis",
            "time-micros",
            "timestamp-millis",
            "timestamp-micros",
            "local-timestamp-millis",
            "local-timestamp-micros",
            "duration",
            "uuid"
        ];

    private static readonly IEnumerable<Schema.Type> ValueTypes =
    [
            Schema.Type.Boolean,
            Schema.Type.Int,
            Schema.Type.Long,
            Schema.Type.Float,
            Schema.Type.Double,
            Schema.Type.Enumeration
        ];

    public static FieldDeclarationSyntax CreateProtocolDefinition(string json)
    {
        return FieldDeclaration(
            VariableDeclaration(
                IdentifierName("AvroProtocol"))
            .WithVariables(
                SingletonSeparatedList(
                    VariableDeclarator(
                        Identifier("_protocol"))
                    .WithInitializer(
                        EqualsValueClause(
                            InvocationExpression(
                                MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    IdentifierName("AvroProtocol"),
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

    public static PropertyDeclarationSyntax CreateProtocolProperty()
    {
        return PropertyDeclaration(
                IdentifierName("AvroProtocol"),
                Identifier("Protocol"))
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
                    IdentifierName("_protocol")))
            .WithSemicolonToken(
                Token(SyntaxKind.SemicolonToken))
            .WithTrailingTrivia(TriviaList(CarriageReturnLineFeed, CarriageReturnLineFeed));
    }

    public static FieldDeclarationSyntax CreateSchemaDefinition(string json)
    {
        return FieldDeclaration(
            VariableDeclaration(
                IdentifierName("AvroSchema"))
            .WithVariables(
                SingletonSeparatedList(
                    VariableDeclarator(
                        Identifier("_schema"))
                    .WithInitializer(
                        EqualsValueClause(
                            InvocationExpression(
                                MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    IdentifierName("AvroSchema"),
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

    public static PropertyDeclarationSyntax CreateSchemaProperty()
    {
        return PropertyDeclaration(
                IdentifierName("AvroSchema"),
                Identifier("Schema"))
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
                    IdentifierName("_schema")))
            .WithSemicolonToken(
                Token(SyntaxKind.SemicolonToken))
            .WithTrailingTrivia(TriviaList(CarriageReturnLineFeed, CarriageReturnLineFeed));
    }
}