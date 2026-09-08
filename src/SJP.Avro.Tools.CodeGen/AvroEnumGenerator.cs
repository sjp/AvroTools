using System;
using System.Linq;
using Avro;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace SJP.Avro.Tools.CodeGen;

/// <summary>
/// Generates C# enum types for Avro enumeration types.
/// </summary>
public class AvroEnumGenerator : ICodeGenerator<EnumSchema>
{
    /// <summary>
    /// Creates a C# implementation of an Avro enumeration type.
    /// </summary>
    /// <param name="schema">A definition of an enum in Avro schema.</param>
    /// <param name="baseNamespace">The base namespace to use (when one is absent).</param>
    /// <param name="options">Ignored. Enum types have no properties for output style options to affect.</param>
    /// <returns>A string representing a C# file containing an enum definition.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="schema"/> or <paramref name="baseNamespace"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="baseNamespace"/> is empty or whitespace and <paramref name="schema"/> does not declare a namespace.</exception>
    public string Generate(EnumSchema schema, string baseNamespace, CodeGenOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(baseNamespace);

        var ns = SyntaxUtilities.ResolveNamespace(schema.Namespace, baseNamespace, schema.Fullname);

        var namespaceDeclaration = NamespaceDeclaration(SyntaxUtilities.SafeNamespaceName(ns));

        // Symbols keep their schema order, and each is given the ordinal that order implies.
        // Avro encodes an enum as the position of its symbol, and the specific reader turns that
        // position straight into the C# enum value, so any other numbering would decode to the
        // wrong symbol. A schema-level default is deliberately not hoisted to the front: it is
        // applied by schema resolution when a writer's symbol is unknown to the reader, and does
        // not need to be the C# default value.
        var members = schema.Symbols
            .Select((m, ordinal) => EnumMemberDeclaration(SyntaxUtilities.SafeIdentifier(m))
                .WithEqualsValue(
                    EqualsValueClause(
                        LiteralExpression(
                            SyntaxKind.NumericLiteralExpression,
                            Literal(ordinal)))))
            .ToList();

        var generatedEnum = EnumDeclaration(SyntaxUtilities.SafeIdentifier(schema.Name))
            .AddModifiers(Token(SyntaxKind.PublicKeyword))
            .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
            .WithMembers(SeparatedList(members))
            .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken));

        generatedEnum = generatedEnum
            .WithLeadingTrivia(SyntaxUtilities.BuildCommentTrivia(schema.Documentation));

        var document = CompilationUnit()
            .WithMembers(
                SingletonList<MemberDeclarationSyntax>(
                    namespaceDeclaration
                        .WithMembers(
                            SingletonList<MemberDeclarationSyntax>(generatedEnum))));

        return SyntaxUtilities.Format(document);
    }
}