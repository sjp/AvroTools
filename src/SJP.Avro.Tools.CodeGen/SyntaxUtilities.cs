using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avro;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace SJP.Avro.Tools.CodeGen;

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
        ArgumentNullException.ThrowIfNull(comment);

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
        ArgumentNullException.ThrowIfNull(comment);

        var result = comment.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().TrimStart('*').Trim())
            .ToList();

        // process from a bunch of lines to paragraphs
        var builder = new StringBuilder();
        var paragraphs = new List<string>();

        foreach (var line in result)
        {
            if (string.IsNullOrEmpty(line))
            {
                // done with paragraph
                var paragraph = builder.ToString();
                paragraphs.Add(paragraph);

                builder.Clear();
                continue;
            }

            // Append space to separate between newlines.
            // Avoids the following:
            //
            // Input: 'It was the best of times,\nit was the worst of times'
            // Output: 'It was the best of times,it was the worst of times'
            //
            // We're wanting: 'It was the best of times, it was the worst of times'
            builder
                .Append(' ')
                .Append(line);
        }

        var lastParagraph = builder.ToString();
        if (!string.IsNullOrEmpty(lastParagraph))
            paragraphs.Add(lastParagraph);

        return paragraphs.ConvertAll(p => p.Trim());
    }

    private static readonly SyntaxToken XmlNewline = XmlTextNewLine(Environment.NewLine);

    /// <summary>
    /// A type syntax lookup that translates from built-in C# types to Roslyn type definitions.
    /// </summary>
    public static readonly IReadOnlyDictionary<Schema.Type, TypeSyntax> TypeSyntaxMap = new Dictionary<Schema.Type, TypeSyntax>()
    {
        [Schema.Type.Boolean] = PredefinedType(Token(SyntaxKind.BoolKeyword)),
        [Schema.Type.Bytes] = ArrayType(
            PredefinedType(Token(SyntaxKind.ByteKeyword)),
            SingletonList(ArrayRankSpecifier())),
        [Schema.Type.Double] = PredefinedType(Token(SyntaxKind.DoubleKeyword)),
        [Schema.Type.Float] = PredefinedType(Token(SyntaxKind.FloatKeyword)),
        [Schema.Type.Int] = PredefinedType(Token(SyntaxKind.IntKeyword)),
        [Schema.Type.Long] = PredefinedType(Token(SyntaxKind.LongKeyword)),
        [Schema.Type.Null] = PredefinedType(Token(SyntaxKind.ObjectKeyword)),
        [Schema.Type.String] = PredefinedType(Token(SyntaxKind.StringKeyword))
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