using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using AvroSchema = Avro.Schema;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;


namespace SJP.Avro.Tools.CodeGen
{


    internal static class AvroSchemaUtilities
    {
        public static TypeSyntax GetFieldType(AvroSchema schema)
        {
            var fieldIsNullable = IsNullableRefType(schema) || IsNullableValueType(schema);
            var fieldType = GetSimpleFieldType(schema);
            return fieldIsNullable ? NullableType(fieldType) : fieldType;
        }

        public static TypeSyntax GetSimpleFieldType(AvroSchema schema)
        {
            if (SyntaxUtilities.TypeSyntaxMap.TryGetValue(schema.Tag, out var builtinType))
                return builtinType;

            if (schema is global::Avro.LogicalSchema logicalSchema)
                return ResolveLogicalType(logicalSchema);

            if (schema is global::Avro.ArraySchema arraySchema)
                return ResolveArrayType(arraySchema);

            if (schema is global::Avro.MapSchema mapSchema)
                return ResolveMapType(mapSchema);

            if (schema is global::Avro.UnionSchema unionSchema)
                return ResolveUnionType(unionSchema);

            return IdentifierName(schema.Name);
        }

        private static TypeSyntax ResolveLogicalType(global::Avro.LogicalSchema logicalSchema)
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
                _ => throw new ArgumentOutOfRangeException($"Unable to resolve a type for logicalType of '{ logicalSchema.Name }'")
            };
        }

        private static TypeSyntax ResolveArrayType(global::Avro.ArraySchema arraySchema)
        {
            var value = GetFieldType(arraySchema.ItemSchema);
            return ArrayType(
                value,
                SingletonList(ArrayRankSpecifier()));
        }

        private static TypeSyntax ResolveMapType(global::Avro.MapSchema mapSchema)
        {
            var value = GetFieldType(mapSchema.ValueSchema);
            return GenericName(
                Identifier("IDictionary"))
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

        private static TypeSyntax ResolveUnionType(global::Avro.UnionSchema unionSchema)
        {
            var typeCount = unionSchema.Schemas
                .Select(s => s.Tag)
                .Distinct()
                .Count(t => t != AvroSchema.Type.Null);

            // If we have a set of values > 2, we'll need custom code (not possible
            // to automatically generate.
            // Return 'object'.
            if (typeCount > 1)
                return PredefinedType(Token(SyntaxKind.ObjectKeyword));

            var nonNullType = unionSchema.Schemas
                .FirstOrDefault(s => s.Tag != AvroSchema.Type.Null);

            // giving up, only a null value (unable to resolve to anything other than 'object'.
            if (nonNullType == null)
                return PredefinedType(Token(SyntaxKind.ObjectKeyword));

            return GetFieldType(nonNullType);
        }

        public static bool IsNullableRefType(AvroSchema schema)
        {
            return schema is global::Avro.UnionSchema unionSchema
                && unionSchema.Schemas.Any(s => s.Tag == AvroSchema.Type.Null)
                && !unionSchema.Schemas.Any(s => s.Tag != AvroSchema.Type.Null && IsValueType(schema));
        }

        public static bool IsNullableValueType(AvroSchema schema)
        {
            return schema is global::Avro.UnionSchema unionSchema
                && unionSchema.Schemas.Any(s => s.Tag == AvroSchema.Type.Null)
                && !unionSchema.Schemas.Any(s => s.Tag != AvroSchema.Type.Null && !IsValueType(schema));
        }

        public static bool IsValueType(AvroSchema schema)
        {
            if (ValueTypes.Contains(schema.Tag))
                return true;

            if (schema is global::Avro.LogicalSchema logicalSchema)
                return ValueTypeLogicalTypeNames.Contains(logicalSchema.LogicalTypeName);

            return false;
        }

        private static readonly IEnumerable<string> ValueTypeLogicalTypeNames = new[]
        {
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
        };

        private static readonly IEnumerable<AvroSchema.Type> ValueTypes = new[]
        {
            AvroSchema.Type.Boolean,
            AvroSchema.Type.Int,
            AvroSchema.Type.Long,
            AvroSchema.Type.Float,
            AvroSchema.Type.Double,
            AvroSchema.Type.Enumeration
        };

        public static FieldDeclarationSyntax CreateProtocolDefinition(string json)
        {
            return FieldDeclaration(
                VariableDeclaration(
                    IdentifierName("Protocol"))
                .WithVariables(
                    SingletonSeparatedList(
                        VariableDeclarator(
                            Identifier("_protocol"))
                        .WithInitializer(
                            EqualsValueClause(
                                InvocationExpression(
                                    MemberAccessExpression(
                                        SyntaxKind.SimpleMemberAccessExpression,
                                        MemberAccessExpression(
                                            SyntaxKind.SimpleMemberAccessExpression,
                                            IdentifierName("Avro"),
                                            IdentifierName("Protocol")),
                                        IdentifierName("Parse")))
                                .WithArgumentList(
                                    ArgumentList(
                                        SingletonSeparatedList(
                                            Argument(
                                                LiteralExpression(
                                                    SyntaxKind.StringLiteralExpression,
                                                    Literal(json)))))))))))
                .WithModifiers(
                        TokenList(
                            new[]{
                                Token(SyntaxKind.PrivateKeyword),
                                Token(SyntaxKind.StaticKeyword),
                                Token(SyntaxKind.ReadOnlyKeyword)}));
        }

        public static PropertyDeclarationSyntax CreateProtocolProperty()
        {
            return PropertyDeclaration(
                    IdentifierName("Protocol"),
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
                    IdentifierName("Schema"))
                .WithVariables(
                    SingletonSeparatedList(
                        VariableDeclarator(
                            Identifier("_schema"))
                        .WithInitializer(
                            EqualsValueClause(
                                InvocationExpression(
                                    MemberAccessExpression(
                                        SyntaxKind.SimpleMemberAccessExpression,
                                        MemberAccessExpression(
                                            SyntaxKind.SimpleMemberAccessExpression,
                                            IdentifierName("Avro"),
                                            IdentifierName("Schema")),
                                        IdentifierName("Parse")))
                                .WithArgumentList(
                                    ArgumentList(
                                        SingletonSeparatedList(
                                            Argument(
                                                LiteralExpression(
                                                    SyntaxKind.StringLiteralExpression,
                                                    Literal(json)))))))))))
                .WithModifiers(
                        TokenList(
                            new[]{
                                Token(SyntaxKind.PrivateKeyword),
                                Token(SyntaxKind.StaticKeyword),
                                Token(SyntaxKind.ReadOnlyKeyword)}));
        }

        public static PropertyDeclarationSyntax CreateSchemaProperty()
        {
            return PropertyDeclaration(
                    IdentifierName("Schema"),
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
}
