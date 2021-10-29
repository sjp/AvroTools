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
        public string Generate(string json)
        {
            var schema = Schema.Parse(json);

            var enumSchema = schema as EnumSchema;
            var ns = enumSchema.Namespace;

            var namespaceDeclaration = NamespaceDeclaration(ParseName(ns ?? "FakeExample"));

            var orderedSymbols = enumSchema.Symbols;

            // reorder to place default in front (so that default(Enum) == defaultValue)
            if (enumSchema.Default != null)
            {
                orderedSymbols = new[] { enumSchema.Default }
                    .Concat(orderedSymbols.Where(s => s != enumSchema.Default))
                    .ToList();
            }

            var members = orderedSymbols
                .Select(m => EnumMemberDeclaration(m))
                .ToList();

            var generatedEnum = EnumDeclaration(enumSchema.Name)
                .AddModifiers(Token(SyntaxKind.PublicKeyword))
                .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
                .WithMembers(SeparatedList(members))
                .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken));

            if (enumSchema.Documentation != null)
            {
                generatedEnum = generatedEnum
                    .WithLeadingTrivia(SyntaxUtilities.BuildCommentTrivia(enumSchema.Documentation));
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
