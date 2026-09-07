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
            : [XmlText(XmlTextLiteral(commentLines.Single()), XmlNewline)];
        // add a newline after the summary element
        var formattedCommentNodes = new XmlNodeSyntax[] { XmlText(XmlNewline) }.Concat(commentNodes).ToArray();

        return TriviaList(
            Trivia(
                DocumentationComment(
                    XmlSummaryElement(formattedCommentNodes))),
            ElasticCarriageReturnLineFeed
        );
    }

    private static readonly char[] LineEndingChars = ['\r', '\n'];

    private static IReadOnlyCollection<string> GetLines(string comment)
    {
        ArgumentNullException.ThrowIfNull(comment);

        var result = comment.Split(LineEndingChars, StringSplitOptions.RemoveEmptyEntries)
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
    /// Determines the namespace to declare generated code in. An Avro type's own namespace is
    /// preferred; the base namespace is a fallback for a type that declares none, and is only
    /// required in that case.
    /// </summary>
    /// <param name="declaredNamespace">The namespace declared by an Avro schema or protocol, if any.</param>
    /// <param name="baseNamespace">The base namespace to fall back to.</param>
    /// <param name="typeName">The name of the type being generated, used to explain a missing namespace.</param>
    /// <returns>The namespace to declare the generated type in.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="baseNamespace"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="baseNamespace"/> is empty or whitespace and <paramref name="declaredNamespace"/> is absent.</exception>
    public static string ResolveNamespace(string? declaredNamespace, string baseNamespace, string typeName)
    {
        ArgumentNullException.ThrowIfNull(baseNamespace);

        if (!string.IsNullOrWhiteSpace(declaredNamespace))
            return declaredNamespace;

        if (string.IsNullOrWhiteSpace(baseNamespace))
            throw new ArgumentException($"A base namespace is required because '{typeName}' does not declare one.", nameof(baseNamespace));

        return baseNamespace;
    }

    /// <summary>
    /// Creates an identifier token for a name taken from an Avro schema. Avro names admit every C#
    /// keyword, so a name that is one is emitted with a verbatim <c>@</c> prefix (<c>@class</c>).
    /// The prefix is purely lexical: the declared member still carries the Avro name.
    /// </summary>
    /// <param name="name">A name declared in an Avro schema.</param>
    /// <returns>A token that is always a legal C# identifier.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or whitespace.</exception>
    public static SyntaxToken SafeIdentifier(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!IsKeyword(name))
            return Identifier(name);

        return Identifier(TriviaList(), SyntaxKind.IdentifierToken, "@" + name, name, TriviaList());
    }

    /// <summary>
    /// Creates an identifier name expression for a name taken from an Avro schema, escaping it
    /// where necessary in the same way as <see cref="SafeIdentifier(string)"/>.
    /// </summary>
    /// <param name="name">A name declared in an Avro schema.</param>
    /// <returns>An identifier name that is always a legal C# identifier.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or whitespace.</exception>
    public static IdentifierNameSyntax SafeIdentifierName(string name) => IdentifierName(SafeIdentifier(name));

    /// <summary>
    /// Determines whether a name has to be escaped to be used as an identifier. Reserved keywords
    /// always do. Most contextual keywords do not, and escaping them would only add noise, but the
    /// few that a declaration can begin with are read as a modifier or as a declaration keyword
    /// rather than as a name, so they do.
    /// </summary>
    private static bool IsKeyword(string name)
    {
        return SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None
            || DeclarationContextualKeywords.Contains(name);
    }

    private static readonly IReadOnlySet<string> DeclarationContextualKeywords =
        new HashSet<string>(StringComparer.Ordinal) { "file", "record", "required" };

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
        List(
        [
                AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
                AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
        ])
    );
}