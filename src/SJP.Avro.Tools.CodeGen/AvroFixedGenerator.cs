using System;
using System.Collections.Generic;
using System.Linq;
using Avro;
using Avro.Specific;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace SJP.Avro.Tools.CodeGen;

/// <summary>
/// Generates C# class files for Avro fixed types.
/// </summary>
public class AvroFixedGenerator : ICodeGenerator<FixedSchema>
{
    /// <summary>
    /// Creates a C# implementation of an Avro fixed type.
    /// </summary>
    /// <param name="schema">A definition of a fixed type in Avro schema.</param>
    /// <param name="baseNamespace">The base namespace to use (when one is absent).</param>
    /// <returns>A string representing a C# file containing a class definition.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="schema"/> is <c>null</c> or <paramref name="baseNamespace"/> is <c>null</c>, empty or whitespace.</exception>
    public string Generate(FixedSchema schema, string baseNamespace)
    {
        if (schema == null)
            throw new ArgumentNullException(nameof(schema));
        if (string.IsNullOrWhiteSpace(baseNamespace))
            throw new ArgumentNullException(nameof(baseNamespace));

        var ns = schema.Namespace ?? baseNamespace;

        var namespaceDeclaration = NamespaceDeclaration(ParseName(ns));

        var namespaces = GetRequiredNamespaces();
        var usingStatements = namespaces
            .Select(static ns => ParseName(ns))
            .Select(UsingDirective)
            .ToList();

        var schemaField = AvroSchemaUtilities.CreateSchemaDefinition(schema.ToString());
        var schemaProperty = AvroSchemaUtilities.CreateSchemaProperty()
            .WithModifiers(
                TokenList(
                    Token(SyntaxKind.PublicKeyword),
                    Token(SyntaxKind.OverrideKeyword)));
        var fixedSizeProp = CreateFixedSizeProperty(schema.Size);
        var ctor = CreateConstructor(schema.Name);

        var members = new MemberDeclarationSyntax[]
        {
                schemaField,
                schemaProperty,
                fixedSizeProp,
                ctor
        };

        var generatedRecord = RecordDeclaration(Token(SyntaxKind.RecordKeyword), schema.Name)
            .AddModifiers(Token(SyntaxKind.PublicKeyword))
            .AddBaseListTypes(SimpleBaseType(IdentifierName(nameof(SpecificFixed))))
            .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
            .WithMembers(List(members))
            .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken));

        if (schema.Documentation != null)
        {
            generatedRecord = generatedRecord
                .WithLeadingTrivia(SyntaxUtilities.BuildCommentTrivia(schema.Documentation));
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
