using System;
using System.Collections.Generic;
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
        var fieldIsNullable = IsNullableRefType(schema) || IsNullableValueType(schema);
        var fieldType = GetSimpleFieldType(schema);
        return fieldIsNullable ? NullableType(fieldType) : fieldType;
    }

    public static TypeSyntax GetSimpleFieldType(Schema schema)
    {
        if (SyntaxUtilities.TypeSyntaxMap.TryGetValue(schema.Tag, out var builtinType))
            return builtinType;

        if (schema is LogicalSchema logicalSchema)
            return ResolveLogicalType(logicalSchema);

        if (schema is ArraySchema arraySchema)
            return ResolveArrayType(arraySchema);

        if (schema is MapSchema mapSchema)
            return ResolveMapType(mapSchema);

        if (schema is UnionSchema unionSchema)
            return ResolveUnionType(unionSchema);

        return IdentifierName(schema.Name);
    }

    private static TypeSyntax ResolveLogicalType(LogicalSchema logicalSchema)
    {
        return logicalSchema.LogicalTypeName switch
        {
            "decimal" => PredefinedType(Token(SyntaxKind.DecimalKeyword)),
            "date" => IdentifierName(nameof(DateTime)),
            "time-millis" => IdentifierName(nameof(TimeSpan)),
            "time-micros" => IdentifierName(nameof(TimeSpan)),
            "timestamp-millis" => IdentifierName(nameof(DateTime)),
            "timestamp-micros" => IdentifierName(nameof(DateTime)),
            "local-timestamp-millis" => IdentifierName(nameof(DateTime)),
            "local-timestamp-micros" => IdentifierName(nameof(DateTime)),
            "duration" => IdentifierName(nameof(TimeSpan)),
            "uuid" => IdentifierName(Token(SyntaxKind.StringKeyword)),
            _ => throw new ArgumentOutOfRangeException($"Unable to resolve a type for logicalType of '{logicalSchema.Name}'")
        };
    }

    private static TypeSyntax ResolveArrayType(ArraySchema arraySchema)
    {
        var value = GetFieldType(arraySchema.ItemSchema);
        return GenericName(
            Identifier(nameof(System.Collections.Generic.List<>)))
            .WithTypeArgumentList(
                TypeArgumentList(
                    SingletonSeparatedList(value)));
    }

    private static TypeSyntax ResolveMapType(MapSchema mapSchema)
    {
        var value = GetFieldType(mapSchema.ValueSchema);
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

    private static TypeSyntax ResolveUnionType(UnionSchema unionSchema)
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

        return GetFieldType(nonNullType);
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
            "duration"
            // uuid is a string
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