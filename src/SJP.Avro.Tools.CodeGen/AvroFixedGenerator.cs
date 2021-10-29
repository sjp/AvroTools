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
    public class AvroFixedGenerator
    {
        public string Generate(string json)
        {
            //var json = File.ReadAllText(filePath);
            //var schema = AvroSchema.Parse(json);

            var schema = global::Avro.Schema.Parse(json);

            var fixedSchema = schema as global::Avro.FixedSchema;
            var ns = fixedSchema.Namespace;

            var namespaceDeclaration = NamespaceDeclaration(ParseName(ns ?? "FakeExample"));

            var namespaces = GetRequiredNamespaces();
            var usingStatements = namespaces
                .Select(static ns => ParseName(ns))
                .Select(UsingDirective)
                .ToList();

            var schemaField = AvroSchemaUtilities.CreateSchemaDefinition(fixedSchema.ToString());
            var schemaProperty = AvroSchemaUtilities.CreateSchemaProperty()
                .WithModifiers(
                    TokenList(
                        Token(SyntaxKind.PublicKeyword),
                        Token(SyntaxKind.OverrideKeyword)));
            var fixedSizeProp = CreateFixedSizeProperty(fixedSchema.Size);
            var ctor = CreateConstructor(fixedSchema.Name);

            var members = new MemberDeclarationSyntax[]
            {
                schemaField,
                schemaProperty,
                fixedSizeProp,
                ctor
            };

            var generatedRecord = RecordDeclaration(Token(SyntaxKind.RecordKeyword), fixedSchema.Name)
                .AddModifiers(Token(SyntaxKind.PublicKeyword))
                .AddBaseListTypes(SimpleBaseType(IdentifierName("SpecificFixed")))
                .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
                .WithMembers(List(members))
                .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken));

            if (fixedSchema.Documentation != null)
            {
                generatedRecord = generatedRecord
                    .WithLeadingTrivia(SyntaxUtilities.BuildCommentTrivia(fixedSchema.Documentation));
            }

            var document = CompilationUnit()
                .WithUsings(List(usingStatements))
                .WithMembers(
                    SingletonList<MemberDeclarationSyntax>(
                        namespaceDeclaration
                            .WithMembers(
                                SingletonList<MemberDeclarationSyntax>(generatedRecord))));

            using var workspace = new AdhocWorkspace();
            return Formatter.Format(document, workspace).ToFullString();
        }

        private static IEnumerable<string> GetRequiredNamespaces()
        {
            var namespaces = new[]
            {
                "System",
                "System.Collections.Generic",
                "Avro",
                "Avro.Specific"
            };

            return namespaces.OrderNamespaces();
        }

        private static ConstructorDeclarationSyntax CreateConstructor(string className)
        {
            return ConstructorDeclaration(
                Identifier(className))
                .WithModifiers(
                    TokenList(
                        Token(SyntaxKind.PublicKeyword)))
                .WithInitializer(
                    ConstructorInitializer(
                        SyntaxKind.BaseConstructorInitializer,
                        ArgumentList(
                            SingletonSeparatedList(
                                Argument(
                                    IdentifierName("FixedSize"))))))
                .WithBody(Block());
        }

        private static PropertyDeclarationSyntax CreateFixedSizeProperty(int size)
        {
            return PropertyDeclaration(
                PredefinedType(Token(SyntaxKind.UIntKeyword)),
                    Identifier("FixedSize"))
                .WithModifiers(
                    TokenList(
                        Token(SyntaxKind.PublicKeyword),
                        Token(SyntaxKind.StaticKeyword)))
                .WithAccessorList(
                    AccessorList(
                        SingletonList(
                            AccessorDeclaration(
                                SyntaxKind.GetAccessorDeclaration)
                            .WithSemicolonToken(
                                Token(SyntaxKind.SemicolonToken)))))
                .WithInitializer(
                    EqualsValueClause(LiteralExpression(
                            SyntaxKind.NumericLiteralExpression,
                            Literal(size))))
                .WithSemicolonToken(
                    Token(SyntaxKind.SemicolonToken));
        }
    }
}
