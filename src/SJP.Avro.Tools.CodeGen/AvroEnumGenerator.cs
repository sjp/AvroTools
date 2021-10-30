using System.Linq;
using Avro;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace SJP.Avro.Tools.CodeGen
{
    public class AvroEnumGenerator
    {
        public string Generate(EnumSchema schema, string baseNamespace)
        {
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
