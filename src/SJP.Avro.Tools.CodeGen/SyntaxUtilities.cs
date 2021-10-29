using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using AvroSchema = Avro.Schema;


namespace SJP.Avro.Tools.CodeGen
{
    internal static class SyntaxUtilities
    {
        /// <summary>
        /// Constructs a documentation comment definition for use with Roslyn.
        /// </summary>
        /// <param name="comment">A comment.</param>
        /// <returns>Syntax nodes that represent the comment.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="comment"/> is <c>null</c>.</exception>
        public static SyntaxTriviaList BuildCommentTrivia(string comment)
        {
            if (comment == null)
                throw new ArgumentNullException(nameof(comment));

            var commentLines = GetLines(comment);
            var commentNodes = commentLines.Count > 1
                ? commentLines.SelectMany(static l => new XmlNodeSyntax[] { XmlParaElement(XmlText(l)), XmlText(XmlNewline) }).ToArray()
                : new XmlNodeSyntax[] { XmlText(XmlTextLiteral(commentLines.Single()), XmlNewline) };
            // add a newline after the summary element
            var formattedCommentNodes = new XmlNodeSyntax[] { XmlText(XmlNewline) }.Concat(commentNodes).ToArray();

            return TriviaList(
                Trivia(
                    DocumentationComment(
                        XmlSummaryElement(formattedCommentNodes))),
                ElasticCarriageReturnLineFeed
            );
        }

        private static IReadOnlyCollection<string> GetLines(string comment)
        {
            if (comment == null)
                throw new ArgumentNullException(nameof(comment));

            return comment.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.TrimStart('*').Trim())
                .ToList();
        }

        private static readonly SyntaxToken XmlNewline = XmlTextNewLine(Environment.NewLine);

        /// <summary>
        /// A type syntax lookup that translates from built-in C# types to Roslyn type definitions.
        /// </summary>
        public static readonly IReadOnlyDictionary<AvroSchema.Type, TypeSyntax> TypeSyntaxMap = new Dictionary<AvroSchema.Type, TypeSyntax>()
        {
            [AvroSchema.Type.Boolean] = PredefinedType(Token(SyntaxKind.BoolKeyword)),
            [AvroSchema.Type.Bytes] = ArrayType(
                PredefinedType(Token(SyntaxKind.ByteKeyword)),
                SingletonList(ArrayRankSpecifier())),
            [AvroSchema.Type.Double] = PredefinedType(Token(SyntaxKind.DoubleKeyword)),
            [AvroSchema.Type.Float] = PredefinedType(Token(SyntaxKind.FloatKeyword)),
            [AvroSchema.Type.Int] = PredefinedType(Token(SyntaxKind.IntKeyword)),
            [AvroSchema.Type.Long] = PredefinedType(Token(SyntaxKind.LongKeyword)),
            [AvroSchema.Type.Null] = PredefinedType(Token(SyntaxKind.ObjectKeyword)),
            [AvroSchema.Type.String] = PredefinedType(Token(SyntaxKind.StringKeyword))
        };

        /// <summary>
        /// Returns an assignment expression that generates <c>= default!</c>.
        /// </summary>
        /// <value>A not null default assignment expression.</value>
        public static EqualsValueClauseSyntax NotNullDefault { get; } = EqualsValueClause(
            PostfixUnaryExpression(
                SyntaxKind.SuppressNullableWarningExpression,
                LiteralExpression(
                    SyntaxKind.DefaultLiteralExpression,
                    Token(SyntaxKind.DefaultKeyword))));

        /// <summary>
        /// Returns an expression that generates <c>{ get; set; }</c>.
        /// </summary>
        /// <value>An auto property expression.</value>
        public static AccessorListSyntax PropertyGetSetDeclaration { get; } = AccessorList(
            List(new[]
            {
                AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
                AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
            })
        );
    }
}
