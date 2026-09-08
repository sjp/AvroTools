using System;
using System.Collections.Generic;
using System.Linq;
using Avro;
using Avro.Specific;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

        var typeName = protocol.Name;
        var protocolFieldName = ReservedNames.MakeAvailable(
            AvroSchemaUtilities.ProtocolFieldName,
            new HashSet<string>(StringComparer.Ordinal) { typeName });

        var protocolField = AvroSchemaUtilities.CreateProtocolDefinition(AvroSchemaUtilities.ToPortableJson(protocol.ToString()), protocolFieldName);
        var protocolProperty = AvroSchemaUtilities.CreateProtocolProperty(protocolFieldName);

        var requestMethod = BuildRequestMethod(protocol, ns);

        // A C# type may not declare a member of its own name, so a protocol named after one of the
        // two members ISpecificProtocol obliges it to carry implements that member explicitly
        // instead. The protocol keeps the name the Avro definition gave it either way.
        if (string.Equals(typeName, AvroSchemaUtilities.ProtocolMemberName, StringComparison.Ordinal))
            protocolProperty = SyntaxUtilities.AsExplicitImplementation(protocolProperty, typeof(ISpecificProtocol));
        if (string.Equals(typeName, nameof(ISpecificProtocol.Request), StringComparison.Ordinal))
            requestMethod = SyntaxUtilities.AsExplicitImplementation(requestMethod, typeof(ISpecificProtocol));

        var methodNames = BuildMethodNames(protocol, typeName, protocolFieldName);
        var messageMethods = protocol.Messages.Values
            .Select(m => BuildMethod(m, methodNames[m.Name], ns))
            .ToList();

        var members = new MemberDeclarationSyntax[]
        {
                protocolField,
                protocolProperty,
                requestMethod
        }.Concat(messageMethods)
        .ToList();

        var generatedRecord = RecordDeclaration(Token(SyntaxKind.RecordKeyword), SyntaxUtilities.SafeIdentifier(typeName))
            .AddModifiers(
                Token(SyntaxKind.PublicKeyword),
                Token(SyntaxKind.AbstractKeyword))
            .AddBaseListTypes(SimpleBaseType(SyntaxUtilities.GlobalName(typeof(ISpecificProtocol))))
            .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
            .WithMembers(List(members))
            .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken));

        generatedRecord = generatedRecord
            .WithLeadingTrivia(SyntaxUtilities.BuildCommentTrivia(protocol.Doc));

        return SyntaxUtilities.GenerateDocument(namespaceDeclaration, generatedRecord);
    }

    /// <summary>
    /// Computes the C# method name for each message. A method may not share its name with the type
    /// that declares it, nor with the other members generated alongside it, nor with a member the
    /// generated record inherits or the compiler writes into it, so a message with one of those
    /// names gets an underscore-suffixed method. The Avro name is unaffected: it stays in the
    /// protocol and in the <c>Request</c> dispatch.
    /// </summary>
    private static IReadOnlyDictionary<string, string> BuildMethodNames(Protocol protocol, string typeName, string protocolFieldName)
    {
        var unavailableNames = new HashSet<string>(StringComparer.Ordinal)
        {
            typeName,
            protocolFieldName,
            AvroSchemaUtilities.ProtocolMemberName,
            nameof(ISpecificProtocol.Request)
        };

        unavailableNames.UnionWith(ReservedNames.ObjectMembers);
        unavailableNames.UnionWith(ReservedNames.RecordMembers);

        var methodNames = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var message in protocol.Messages.Values)
        {
            var candidate = ReservedNames.MakeAvailable(message.Name, unavailableNames);

            methodNames[message.Name] = candidate;
            unavailableNames.Add(candidate);
        }

        return methodNames;
    }

    private static MethodDeclarationSyntax BuildRequestMethod(Protocol protocol, string containingNamespace)
    {
        var messageCases = protocol.Messages.Values
            .Select(m => BuildRequestMethodCase(m, containingNamespace))
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
                                    SyntaxUtilities.GlobalName(typeof(ICallbackRequestor))),
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

    private static SwitchSectionSyntax BuildRequestMethodCase(Message message, string containingNamespace)
    {
        var responseType = AvroSchemaUtilities.GetRuntimeFieldType(message.Response, containingNamespace);

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

    private static MethodDeclarationSyntax BuildMethod(Message message, string methodName, string containingNamespace)
    {
        var responseType = GetMessageResponseType(message.Response, containingNamespace);

        var parameterList = ParameterList(
            SeparatedList(
                message.Request.Fields
                    .ConvertAll(f => BuildMessageParameter(f, containingNamespace))));

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

        method = method
            .WithLeadingTrivia(SyntaxUtilities.BuildCommentTrivia(message.Doc));

        return method;
    }

    /// <summary>
    /// Builds a parameter of a message method. Every value crosses the protocol boundary in the
    /// representation Avro itself uses: the requestor packs the arguments into the request and
    /// unpacks the response with nothing generated in between to convert them, so a decimal is
    /// exchanged as an <c>AvroDecimal</c> rather than as a C# <c>decimal</c>.
    /// </summary>
    private static ParameterSyntax BuildMessageParameter(Field field, string containingNamespace)
    {
        var paramType = AvroSchemaUtilities.GetRuntimeFieldType(field.Schema, containingNamespace);
        var paramName = SyntaxUtilities.SafeIdentifier(field.Name);

        return Parameter(paramName)
            .WithType(paramType);
    }

    private static TypeSyntax GetMessageResponseType(Schema schema, string containingNamespace)
    {
        return schema.Tag == Schema.Type.Null
            ? PredefinedType(Token(SyntaxKind.VoidKeyword))
            : AvroSchemaUtilities.GetRuntimeFieldType(schema, containingNamespace);
    }
}