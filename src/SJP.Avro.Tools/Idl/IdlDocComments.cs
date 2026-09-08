using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// The documentation attached to each declaration in a parsed IDL document.
/// </summary>
/// <remarks>
/// Doc comments are tokenised onto a hidden channel, so they never appear in the parse tree.
/// A declaration is documented by the doc comment that immediately precedes it, with nothing
/// but whitespace and other comments in between. Any other doc comment describes nothing and
/// is reported as a warning rather than rejected, so that a stray comment does not make an
/// otherwise valid document unreadable.
/// </remarks>
internal sealed class IdlDocComments
{
    /// <summary>
    /// The documentation of a document that has none, used where no document has been parsed.
    /// </summary>
    public static IdlDocComments Empty { get; } = new(new Dictionary<int, string>(), []);

    private readonly IReadOnlyDictionary<int, string> _documentation;

    private IdlDocComments(IReadOnlyDictionary<int, string> documentation, IReadOnlyList<string> warnings)
    {
        _documentation = documentation;
        Warnings = warnings;
    }

    /// <summary>
    /// Describes each doc comment that was written where no declaration could claim it.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>
    /// The documentation written against a declaration, or <c>null</c> when it has none.
    /// </summary>
    /// <param name="context">A declaration from the document these comments were resolved from.</param>
    public string? For(ParserRuleContext? context)
    {
        return context != null && _documentation.TryGetValue(context.Start.TokenIndex, out var doc)
            ? doc
            : null;
    }

    /// <summary>
    /// Attaches the doc comments in a token stream to the declarations of a parsed document.
    /// </summary>
    /// <param name="tokens">The token stream the document was parsed from, including hidden tokens.</param>
    /// <param name="document">The parsed document.</param>
    public static IdlDocComments Resolve(CommonTokenStream tokens, IdlParser.IdlFileContext document)
    {
        var documentation = new Dictionary<int, string>();
        var warnings = new List<string>();
        var allTokens = tokens.GetTokens();

        // a declaration claims the doc comment immediately to its left; everything is visited in
        // source order so that each comment is offered to the nearest declaration that follows it
        var lastClaimedIndex = -1;
        foreach (var declaration in FindDocumentableDeclarations(document))
        {
            var declarationIndex = declaration.Start.TokenIndex;
            var precedingComments = tokens.GetHiddenTokensToLeft(declarationIndex, TokenConstants.HiddenChannel);

            var docToken = precedingComments?.Count > 0
                ? precedingComments[^1]
                : null;
            var strayEndIndex = docToken != null
                ? docToken.TokenIndex - 1
                : declarationIndex;

            WarnAboutStrayComments(allTokens, lastClaimedIndex + 1, strayEndIndex, warnings);
            lastClaimedIndex = declarationIndex;

            var doc = docToken.ExtractDocumentation();
            if (doc != null)
                documentation[declarationIndex] = doc;
        }

        // anything left over trails the last declaration, so nothing can claim it either
        WarnAboutStrayComments(allTokens, lastClaimedIndex + 1, allTokens.Count - 1, warnings);

        return new IdlDocComments(documentation, warnings);
    }

    /// <summary>
    /// The declarations that may carry documentation, in source order.
    /// </summary>
    private static IEnumerable<ParserRuleContext> FindDocumentableDeclarations(IdlParser.IdlFileContext document)
    {
        var collector = new DocumentableDeclarationCollector();
        ParseTreeWalker.Default.Walk(collector, document);

        return collector.Declarations.OrderBy(d => d.Start.TokenIndex);
    }

    private static void WarnAboutStrayComments(IList<IToken> tokens, int startIndex, int endIndex, List<string> warnings)
    {
        for (var i = Math.Max(startIndex, 0); i <= endIndex && i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Type != IdlParser.DocComment)
                continue;

            warnings.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Line {0}, char {1}: Ignoring out-of-place documentation comment. Did you mean to use a multiline comment ( /* ... */ ) instead?",
                token.Line,
                token.Column + 1));
        }
    }

    /// <summary>
    /// Gathers every declaration whose translation reads a doc comment. Enum symbols are absent
    /// because their documentation is not carried into the translated schema.
    /// </summary>
    private sealed class DocumentableDeclarationCollector : IdlBaseListener
    {
        public List<ParserRuleContext> Declarations { get; } = [];

        public override void EnterProtocolDeclaration(IdlParser.ProtocolDeclarationContext context) => Declarations.Add(context);

        public override void EnterFixedDeclaration(IdlParser.FixedDeclarationContext context) => Declarations.Add(context);

        public override void EnterEnumDeclaration(IdlParser.EnumDeclarationContext context) => Declarations.Add(context);

        public override void EnterRecordDeclaration(IdlParser.RecordDeclarationContext context) => Declarations.Add(context);

        public override void EnterFieldDeclaration(IdlParser.FieldDeclarationContext context) => Declarations.Add(context);

        public override void EnterVariableDeclaration(IdlParser.VariableDeclarationContext context) => Declarations.Add(context);

        public override void EnterMessageDeclaration(IdlParser.MessageDeclarationContext context) => Declarations.Add(context);

        public override void EnterFormalParameter(IdlParser.FormalParameterContext context) => Declarations.Add(context);
    }
}
