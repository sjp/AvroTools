using System;
using System.Collections.Generic;
using System.Linq;
using Avro;
using Avro.Specific;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace SJP.Avro.Tools.CodeGen;

/// <summary>
/// Generates C# class files for Avro protocol methods.
/// </summary>
public class AvroProtocolGenerator : ICodeGenerator<Protocol>
{
    /// <summary>
    /// Creates a C# implementation of an Avro protocol.
    /// </summary>
    /// <param name="protocol">A definition of an Avro protocol.</param>
    /// <param name="baseNamespace">The base namespace to use (when one is absent).</param>
    /// <param name="options">Ignored. Protocols generate abstract methods rather than properties, so output style options have no effect.</param>
    /// <returns>A string representing a C# file containing a class definition. Empty when no messages are present in the protocol.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="protocol"/> is <c>null</c> or <paramref name="baseNamespace"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="baseNamespace"/> is empty or whitespace and <paramref name="protocol"/> does not declare a namespace.</exception>
    public string Generate(Protocol protocol, string baseNamespace, CodeGenOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        ArgumentNullException.ThrowIfNull(baseNamespace);

        // no messages to generate
        if (protocol.Messages.Count == 0)
            return string.Empty;

        var ns = SyntaxUtilities.ResolveNamespace(protocol.Namespace, baseNamespace, protocol.Name);

        var namespaceDeclaration = NamespaceDeclaration(SyntaxUtilities.SafeNamespaceName(ns));

        var protocolField = AvroSchemaUtilities.CreateProtocolDefinition(protocol.ToString());
        var protocolProperty = AvroSchemaUtilities.CreateProtocolProperty();

        var requestMethod = BuildRequestMethod(protocol);
        var namespaces = GetRequiredNamespaces(protocol);
        var usingStatements = namespaces
            .Select(SyntaxUtilities.SafeNamespaceName)
            .Select(UsingDirective)
            .ToList();

        // prefer alias to avoid conflicts with user types
        var protocolAlias = UsingDirective(
            NameEquals(IdentifierName("AvroProtocol")),
            ParseName("Avro.Protocol"));
        usingStatements.Add(protocolAlias);

        var methodNames = BuildMethodNames(protocol);
        var messageMethods = protocol.Messages.Values
            .Select(m => BuildMethod(m, methodNames[m.Name]))
            .ToList();

        var members = new MemberDeclarationSyntax[]
        {
                protocolField,
                protocolProperty,
                requestMethod
        }.Concat(messageMethods)
        .ToList();

        var generatedRecord = RecordDeclaration(Token(SyntaxKind.RecordKeyword), SyntaxUtilities.SafeIdentifier(protocol.Name))
            .AddModifiers(
                Token(SyntaxKind.PublicKeyword),
                Token(SyntaxKind.AbstractKeyword))
            .AddBaseListTypes(SimpleBaseType(IdentifierName(nameof(ISpecificProtocol))))
            .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
            .WithMembers(List(members))
            .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken));

        if (protocol.Doc != null)
        {
            generatedRecord = generatedRecord
                .WithLeadingTrivia(SyntaxUtilities.BuildCommentTrivia(protocol.Doc));
        }

        var document = CompilationUnit()
            .WithUsings(List(usingStatements))
            .WithMembers(
                SingletonList<MemberDeclarationSyntax>(
                    namespaceDeclaration
                        .WithMembers(
                            SingletonList<MemberDeclarationSyntax>(generatedRecord))));

        using var workspace = new AdhocWorkspace();
        return Formatter.Format(document, workspace).ToFullString();
    }

    /// <summary>
    /// Computes the C# method name for each message. A method may not share its name with the type
    /// that declares it, nor with the other members generated alongside it, so a message with one of
    /// those names gets an underscore-suffixed method. The Avro name is unaffected: it stays in the
    /// protocol and in the <c>Request</c> dispatch.
    /// </summary>
    private static IReadOnlyDictionary<string, string> BuildMethodNames(Protocol protocol)
    {
        var unavailableNames = new HashSet<string>(StringComparer.Ordinal)
        {
            protocol.Name,
            "_protocol",
            nameof(Protocol),
            nameof(ISpecificProtocol.Request)
        };

        var methodNames = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var message in protocol.Messages.Values)
        {
            var candidate = message.Name;
            while (unavailableNames.Contains(candidate))
                candidate += "_";

            methodNames[message.Name] = candidate;
            unavailableNames.Add(candidate);
        }

        return methodNames;
    }

    private static IEnumerable<string> GetRequiredNamespaces(Protocol protocol)
    {
        var systemNamespaces = new[]
        {
            "System",
            "System.Collections.Generic"
        };

        var avroNamespaces = new[]
        {
            "Avro",
            "Avro.IO",
            "Avro.Specific"
        };

        var namespaces = new HashSet<string>(systemNamespaces.Concat(avroNamespaces));

        var baseNamespace = protocol.Namespace;

        foreach (var message in protocol.Messages.Values)
        {
            var responseNamespaces = GetNamespacesForType(message.Response);
            var requestNamespaces = message.Request.Fields
                .Select(f => f.Schema)
                .SelectMany(GetNamespacesForType);

            var scannedNamespaces = responseNamespaces
                .Concat(requestNamespaces)
                .Where(ns => ns != baseNamespace);

            foreach (var ns in scannedNamespaces)
                namespaces.Add(ns);
        }

        return namespaces.OrderNamespaces();
    }

    private static IEnumerable<string> GetNamespacesForType(Schema schema)
    {
        return schema switch
        {
            ArraySchema arraySchema => GetNamespacesForType(arraySchema.ItemSchema),
            MapSchema mapSchema => GetNamespacesForType(mapSchema.ValueSchema),
            UnionSchema unionSchema => unionSchema.Schemas.SelectMany(GetNamespacesForType),
            NamedSchema namedSchema => namedSchema.Namespace != null ? [namedSchema.Namespace] : Array.Empty<string>(),
            _ => []
        };
    }

    private static MethodDeclarationSyntax BuildRequestMethod(Protocol protocol)
    {
        var messageCases = protocol.Messages.Values
            .Select(BuildRequestMethodCase)
            .ToList();

        return MethodDeclaration(
                PredefinedType(
                    Token(SyntaxKind.VoidKeyword)),
                Identifier(nameof(ISpecificProtocol.Request)))
            .WithModifiers(
                TokenList(
                    Token(SyntaxKind.PublicKeyword)))
            .WithParameterList(
                ParameterList(
                    SeparatedList<ParameterSyntax>(
                        new SyntaxNodeOrToken[]
                        {
                                Parameter(
                                    Identifier("requestor"))
                                .WithType(
                                    IdentifierName(nameof(ICallbackRequestor))),
                                Token(SyntaxKind.CommaToken),
                                Parameter(
                                    Identifier("messageName"))
                                .WithType(
                                    PredefinedType(
                                        Token(SyntaxKind.StringKeyword))),
                                Token(SyntaxKind.CommaToken),
                                Parameter(
                                    Identifier("args"))
                                .WithType(
                                    ArrayType(
                                        PredefinedType(
                                            Token(SyntaxKind.ObjectKeyword)))
                                    .WithRankSpecifiers(
                                        SingletonList(
                                            ArrayRankSpecifier()))),
                                Token(SyntaxKind.CommaToken),
                                Parameter(
                                    Identifier("callback"))
                                .WithType(
                                    PredefinedType(
                                        Token(SyntaxKind.ObjectKeyword)))
                        })))
            .WithBody(
                Block(
                    SwitchStatement(
                        IdentifierName("messageName"))
                    .WithSections(
                        List(messageCases))));
    }

    private static SwitchSectionSyntax BuildRequestMethodCase(Message message)
    {
        var responseType = AvroSchemaUtilities.GetFieldType(message.Response);

        return SwitchSection()
            .WithLabels(
                SingletonList<SwitchLabelSyntax>(
                    CaseSwitchLabel(
                        LiteralExpression(
                            SyntaxKind.StringLiteralExpression,
                            Literal(message.Name)))))
            .WithStatements(
                List(
                    new StatementSyntax[]{
                            ExpressionStatement(
                                InvocationExpression(
                                    MemberAccessExpression(
                                        SyntaxKind.SimpleMemberAccessExpression,
                                        IdentifierName("requestor"),
                                        GenericName(
                                            Identifier(nameof(ICallbackRequestor.Request)))
                                        .WithTypeArgumentList(
                                            TypeArgumentList(
                                                SingletonSeparatedList(responseType)))))
                                .WithArgumentList(
                                    ArgumentList(
                                        SeparatedList<ArgumentSyntax>(
                                            new SyntaxNodeOrToken[]
                                            {
                                                Argument(
                                                    IdentifierName("messageName")),
                                                Token(SyntaxKind.CommaToken),
                                                Argument(
                                                    IdentifierName("args")),
                                                Token(SyntaxKind.CommaToken),
                                                Argument(
                                                    IdentifierName("callback"))
                                            })))),
                            BreakStatement()}));
    }

    private static MethodDeclarationSyntax BuildMethod(Message message, string methodName)
    {
        var responseType = GetMessageResponseType(message.Response);

        var parameterList = ParameterList(
            SeparatedList(
                message.Request.Fields
                    .ConvertAll(BuildMessageParameter)));

        var method = MethodDeclaration(
                responseType,
                SyntaxUtilities.SafeIdentifier(methodName))
            .WithModifiers(
                TokenList(
                    Token(SyntaxKind.PublicKeyword),
                    Token(SyntaxKind.AbstractKeyword)))
            .WithParameterList(parameterList)
            .WithSemicolonToken(
                Token(SyntaxKind.SemicolonToken))
            .WithTrailingTrivia(TriviaList(CarriageReturnLineFeed, CarriageReturnLineFeed));

        if (message.Doc != null)
        {
            method = method
                .WithLeadingTrivia(SyntaxUtilities.BuildCommentTrivia(message.Doc));
        }

        return method;
    }

    private static ParameterSyntax BuildMessageParameter(Field field)
    {
        var paramType = AvroSchemaUtilities.GetFieldType(field.Schema);
        var paramName = SyntaxUtilities.SafeIdentifier(field.Name);

        return Parameter(paramName)
            .WithType(paramType);
    }

    private static TypeSyntax GetMessageResponseType(Schema schema)
    {
        return schema.Tag == Schema.Type.Null
            ? PredefinedType(Token(SyntaxKind.VoidKeyword))
            : AvroSchemaUtilities.GetFieldType(schema);
    }
}