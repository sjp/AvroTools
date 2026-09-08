using System;
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
    /// <param name="options">Ignored. Fixed types have no per-field properties for output style options to affect.</param>
    /// <returns>A string representing a C# file containing a class definition.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="schema"/> or <paramref name="baseNamespace"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="baseNamespace"/> is empty or whitespace and <paramref name="schema"/> does not declare a namespace.</exception>
    public string Generate(FixedSchema schema, string baseNamespace, CodeGenOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(baseNamespace);

        var ns = SyntaxUtilities.ResolveNamespace(schema.Namespace, baseNamespace, schema.Fullname);

        var namespaceDeclaration = NamespaceDeclaration(SyntaxUtilities.SafeNamespaceName(ns));

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

        var generatedClass = ClassDeclaration(SyntaxUtilities.SafeIdentifier(schema.Name))
            .AddModifiers(Token(SyntaxKind.PublicKeyword))
            .AddBaseListTypes(SimpleBaseType(SyntaxUtilities.GlobalName(typeof(SpecificFixed))))
            .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
            .WithMembers(List(members))
            .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken));

        generatedClass = generatedClass
            .WithLeadingTrivia(SyntaxUtilities.BuildCommentTrivia(schema.Documentation));

        var document = CompilationUnit()
            .WithMembers(
                SingletonList<MemberDeclarationSyntax>(
                    namespaceDeclaration
                        .WithMembers(
                            SingletonList<MemberDeclarationSyntax>(generatedClass))));

        using var workspace = new AdhocWorkspace();
        return Formatter.Format(document, workspace).ToFullString();
    }

    private static ConstructorDeclarationSyntax CreateConstructor(string className)
    {
        return ConstructorDeclaration(
            SyntaxUtilities.SafeIdentifier(className))
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