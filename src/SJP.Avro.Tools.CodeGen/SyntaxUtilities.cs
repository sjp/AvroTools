using System;
using System.Collections.Frozen;
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
    /// Constructs a documentation comment definition for use with Roslyn. A comment that carries
    /// no text once its line prefixes are stripped documents nothing, so it produces no trivia.
    /// </summary>
    /// <param name="comment">A comment, if any.</param>
    /// <returns>Syntax nodes that represent the comment, or no trivia when the comment is empty.</returns>
    public static SyntaxTriviaList BuildCommentTrivia(string? comment)
    {
        var commentLines = GetLines(comment);
        if (commentLines.Count == 0)
            return TriviaList();

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

    private static IReadOnlyCollection<string> GetLines(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return [];

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
                // done with paragraph, unless nothing has been written to it. A line that is
                // blank once its leading asterisks are stripped separates paragraphs; it does
                // not introduce an empty one.
                var paragraph = builder.ToString();
                if (!string.IsNullOrEmpty(paragraph))
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

    /// <summary>
    /// The line ending used throughout generated source. It is fixed rather than taken from the
    /// host so that the same schema produces byte-for-byte identical output on every platform,
    /// and generated files kept in source control do not differ between contributors.
    /// </summary>
    private const string GeneratedNewLine = "\n";

    private static readonly SyntaxToken XmlNewline = XmlTextNewLine(GeneratedNewLine);

    /// <summary>
    /// Formats a generated compilation unit into C# source text.
    /// </summary>
    /// <param name="document">The compilation unit to format.</param>
    /// <returns>The formatted source text of <paramref name="document"/>.</returns>
    public static string Format(CompilationUnitSyntax document)
    {
        using var workspace = new AdhocWorkspace();

        var options = workspace.Options
            .WithChangedOption(FormattingOptions.NewLine, LanguageNames.CSharp, GeneratedNewLine);

        return Formatter.Format(document, workspace, options).ToFullString();
    }

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

        return EscapedIdentifier(name);
    }

    private static SyntaxToken EscapedIdentifier(string name) =>
        Identifier(TriviaList(), SyntaxKind.IdentifierToken, "@" + name, name, TriviaList());

    /// <summary>
    /// Creates a namespace name from a dot-separated namespace, for use in a namespace declaration
    /// or a using directive. Avro namespaces admit segments that are C# keywords
    /// (<c>avro.test.enum</c>), which have to be emitted verbatim (<c>avro.test.@enum</c>) for the
    /// declaration or directive to parse at all. A segment that arrives already escaped is left as
    /// it is rather than escaped twice.
    /// </summary>
    /// <param name="namespaceName">A dot-separated namespace.</param>
    /// <returns>A name that is always a legal C# namespace name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="namespaceName"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="namespaceName"/> is empty or whitespace, or contains an empty segment.</exception>
    public static NameSyntax SafeNamespaceName(string namespaceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceName);

        var segments = namespaceName.Split('.');

        NameSyntax result = SafeNamespaceSegment(segments[0]);
        for (var i = 1; i < segments.Length; i++)
            result = QualifiedName(result, SafeNamespaceSegment(segments[i]));

        return result;
    }

    private static IdentifierNameSyntax SafeNamespaceSegment(string segment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        var name = segment.StartsWith('@') ? segment[1..] : segment;

        // Only reserved keywords need escaping here. A namespace declaration or a using directive
        // is already committed to reading a name, so a contextual keyword that would be taken for
        // a modifier at the start of a declaration is unambiguous as a namespace segment.
        return IdentifierName(
            SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None
                ? Identifier(name)
                : EscapedIdentifier(name));
    }

    /// <summary>
    /// Creates a name that is rooted in the global namespace, so that it binds to the intended type
    /// whatever else is in scope. Generated code declares members named after an Avro schema, and an
    /// Avro name may be anything at all: a type called <c>Schema</c>, a field called <c>Math</c> or
    /// two types of the same name in different namespaces would otherwise be ambiguous with, or
    /// shadow, the names the generated bodies rely on.
    /// </summary>
    /// <param name="containingNamespace">The dot-separated namespace the name lives in.</param>
    /// <param name="name">The name within that namespace.</param>
    /// <returns>A name of the form <c>global::containing.namespace.Name</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="containingNamespace"/> or <paramref name="name"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="containingNamespace"/> is empty or whitespace, or contains an empty segment.</exception>
    public static NameSyntax GlobalName(string containingNamespace, SimpleNameSyntax name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containingNamespace);
        ArgumentNullException.ThrowIfNull(name);

        var segments = containingNamespace.Split('.');

        NameSyntax result = AliasQualifiedName(
            IdentifierName(Token(SyntaxKind.GlobalKeyword)),
            SafeNamespaceSegment(segments[0]));
        for (var i = 1; i < segments.Length; i++)
            result = QualifiedName(result, SafeNamespaceSegment(segments[i]));

        return QualifiedName(result, name);
    }

    /// <summary>
    /// Creates a global name for a type the generated code refers to by way of the library it is
    /// compiled against, as opposed to one named by an Avro schema.
    /// </summary>
    /// <param name="type">A non-generic type.</param>
    /// <returns>A name of the form <c>global::containing.namespace.Name</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is <c>null</c>.</exception>
    public static NameSyntax GlobalName(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return GlobalName(type.Namespace!, IdentifierName(type.Name));
    }

    /// <summary>
    /// Creates a global name for a constructed generic type, such as
    /// <c>global::System.Collections.Generic.IList&lt;int&gt;</c>.
    /// </summary>
    /// <param name="genericTypeDefinition">An open generic type, e.g. <c>typeof(IList&lt;&gt;)</c>.</param>
    /// <param name="typeArguments">The type arguments to construct it with.</param>
    /// <returns>A name of the form <c>global::containing.namespace.Name&lt;arguments&gt;</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="genericTypeDefinition"/> or <paramref name="typeArguments"/> is <c>null</c>.</exception>
    public static NameSyntax GlobalGenericName(Type genericTypeDefinition, params TypeSyntax[] typeArguments)
    {
        ArgumentNullException.ThrowIfNull(genericTypeDefinition);
        ArgumentNullException.ThrowIfNull(typeArguments);

        // Reflection spells the arity into the name of a generic type ("IList`1"); C# spells it out
        // as the type argument list instead.
        var name = genericTypeDefinition.Name;
        var arityIndex = name.IndexOf('`', StringComparison.Ordinal);
        if (arityIndex >= 0)
            name = name[..arityIndex];

        var genericName = GenericName(Identifier(name))
            .WithTypeArgumentList(TypeArgumentList(SeparatedList(typeArguments)));

        return GlobalName(genericTypeDefinition.Namespace!, genericName);
    }

    /// <summary>
    /// Restates a member as an explicit implementation of an interface member. A generated type
    /// named after a member it is obliged to carry cannot declare that member ordinarily, because a
    /// C# type may not declare a member of its own name, but it may still implement it explicitly.
    /// Doing so keeps the schema's own name on the generated type, which is what Avro looks a type
    /// up by when it reads a value nested inside another.
    /// </summary>
    /// <param name="property">The member as it would be declared were the name free.</param>
    /// <param name="interfaceType">The interface that declares the member.</param>
    /// <returns>The same member, implemented explicitly.</returns>
    public static PropertyDeclarationSyntax AsExplicitImplementation(PropertyDeclarationSyntax property, Type interfaceType)
    {
        // An explicit implementation is reached through the interface and takes no accessibility of
        // its own, so the modifiers the member would otherwise carry are dropped.
        return property
            .WithModifiers(TokenList())
            .WithExplicitInterfaceSpecifier(ExplicitInterfaceSpecifier(GlobalName(interfaceType)));
    }

    /// <inheritdoc cref="AsExplicitImplementation(PropertyDeclarationSyntax, Type)"/>
    /// <param name="method">The member as it would be declared were the name free.</param>
    /// <param name="interfaceType">The interface that declares the member.</param>
    public static MethodDeclarationSyntax AsExplicitImplementation(MethodDeclarationSyntax method, Type interfaceType)
    {
        return method
            .WithModifiers(TokenList())
            .WithExplicitInterfaceSpecifier(ExplicitInterfaceSpecifier(GlobalName(interfaceType)));
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

    private static readonly FrozenSet<string> DeclarationContextualKeywords =
        new HashSet<string>(StringComparer.Ordinal) { "file", "record", "required" }.ToFrozenSet();

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
    }.ToFrozenDictionary();

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