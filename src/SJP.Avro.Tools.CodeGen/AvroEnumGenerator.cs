using System;
using System.Linq;
using Avro;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace SJP.Avro.Tools.CodeGen
{
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
        /// <returns>A string representing a C# file containing an enum definition.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="schema"/> is <c>null</c> or <paramref name="baseNamespace"/> is <c>null</c>, empty or whitespace.</exception>
        public string Generate(EnumSchema schema, string baseNamespace)
        {
            if (schema == null)
                throw new ArgumentNullException(nameof(schema));
            if (string.IsNullOrWhiteSpace(baseNamespace))
                throw new ArgumentNullException(nameof(baseNamespace));

            var ns = schema.Namespace ?? baseNamespace;

            var namespaceDeclaration = NamespaceDeclaration(ParseName(ns));

            var orderedSymbols = schema.Symbols;

            // reorder to place default in front (so that default(Enum) == defaultValue)
            if (schema.Default != null)
            {
                orderedSymbols = new[] { schema.Default }
                    .Concat(orderedSymbols.Where(s => s != schema.Default))
                    .ToList();
            }

            var members = orderedSymbols
                .Select(m => EnumMemberDeclaration(m))
                .ToList();

            var generatedEnum = EnumDeclaration(schema.Name)
                .AddModifiers(Token(SyntaxKind.PublicKeyword))
                .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
                .WithMembers(SeparatedList(members))
                .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken));

            if (schema.Documentation != null)
            {
                generatedEnum = generatedEnum
                    .WithLeadingTrivia(SyntaxUtilities.BuildCommentTrivia(schema.Documentation));
            }

            var document = CompilationUnit()
                .WithMembers(
                    SingletonList<MemberDeclarationSyntax>(
                        namespaceDeclaration
                            .WithMembers(
                                SingletonList<MemberDeclarationSyntax>(generatedEnum))));

            using var workspace = new AdhocWorkspace();
            return Formatter.Format(document, workspace).ToFullString();
        }
    }
}
