using System;
using System.Collections.Generic;
using System.Linq;
using Avro;
using Avro.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace SJP.Avro.Tools.CodeGen
{
    public class AvroProtocolGenerator
    {
        public string Generate(string json)
        {
            var protocol = Protocol.Parse(json);

            var namespaceDeclaration = NamespaceDeclaration(ParseName(protocol.Namespace ?? "FakeExample"));

            var protocolField = AvroSchemaUtilities.CreateProtocolDefinition(protocol.ToString());
            var protocolProperty = AvroSchemaUtilities.CreateProtocolProperty();

            var requestMethod = BuildRequestMethod(protocol);
            var namespaces = GetRequiredNamespaces(protocol);
            var usingStatements = namespaces
                .Select(static ns => ParseName(ns))
                .Select(UsingDirective)
                .ToList();

            var messageMethods = new List<MethodDeclarationSyntax>();

            foreach (var message in protocol.Messages.Values)
            {
                var messageMethod = BuildMethod(message);
                var messageCallbackMethod = BuildMethodWithCallback(message);

                messageMethods.Add(messageMethod);
                messageMethods.Add(messageCallbackMethod);
            }

            var members = new MemberDeclarationSyntax[]
            {
                protocolField,
                protocolProperty,
                requestMethod
            }.Concat(messageMethods)
            .ToList();

            var generatedRecord = RecordDeclaration(Token(SyntaxKind.RecordKeyword), protocol.Name)
                .AddModifiers(
                    Token(SyntaxKind.PublicKeyword),
                    Token(SyntaxKind.AbstractKeyword))
                .AddBaseListTypes(SimpleBaseType(IdentifierName("ISpecificProtocol")))
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
                NamedSchema namedSchema => namedSchema.Namespace != null ? new[] { namedSchema.Namespace } : Array.Empty<string>(),
                _ => Array.Empty<string>()
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
                    Identifier("Request"))
                .WithModifiers(
                    TokenList(
                        Token(SyntaxKind.PublicKeyword)))
                .WithParameterList(
                    ParameterList(
                        SeparatedList<ParameterSyntax>(
                            new SyntaxNodeOrToken[]{
                                Parameter(
                                    Identifier("requestor"))
                                .WithType(
                                    IdentifierName("ICallbackRequestor")),
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
                                            ArrayRankSpecifier(
                                                SingletonSeparatedList<ExpressionSyntax>(
                                                    OmittedArraySizeExpression()))))),
                                Token(SyntaxKind.CommaToken),
                                Parameter(
                                    Identifier("callback"))
                                .WithType(
                                    PredefinedType(
                                        Token(SyntaxKind.ObjectKeyword)))})))
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
                                            Identifier("Request"))
                                        .WithTypeArgumentList(
                                            TypeArgumentList(
                                                SingletonSeparatedList(responseType)))))
                                .WithArgumentList(
                                    ArgumentList(
                                        SeparatedList<ArgumentSyntax>(
                                            new SyntaxNodeOrToken[]{
                                                Argument(
                                                    IdentifierName("messageName")),
                                                Token(SyntaxKind.CommaToken),
                                                Argument(
                                                    IdentifierName("args")),
                                                Token(SyntaxKind.CommaToken),
                                                Argument(
                                                    IdentifierName("callback"))})))),
                            BreakStatement()}));
        }

        private static MethodDeclarationSyntax BuildMethod(Message message)
        {
            var responseType = GetMessageResponseType(message.Response);

            var parameterList = ParameterList(
                SeparatedList(
                    message.Request.Fields
                        .Select(BuildMessageParameter).ToList()));

            var method = MethodDeclaration(
                    responseType,
                    Identifier(message.Name))
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

        private static MethodDeclarationSyntax BuildMethodWithCallback(Message message)
        {
            var messageParams = message.Request.Fields
                .Select(BuildMessageParameter)
                .Concat(new[] { BuildCallbackParameter(message.Response) })
                .ToList();

            var parameterList = ParameterList(
                SeparatedList(messageParams));

            var method = MethodDeclaration(
                    PredefinedType(Token(SyntaxKind.VoidKeyword)),
                    Identifier(message.Name))
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
            var paramName = Identifier(field.Name);

            return Parameter(paramName)
                .WithType(paramType);
        }

        private static ParameterSyntax BuildCallbackParameter(Schema responseSchema)
        {
            var responseType = GetMessageResponseType(responseSchema);

            var paramType = GenericName(
                Identifier(nameof(ICallback<object>)))
                .WithTypeArgumentList(
                    TypeArgumentList(
                        SingletonSeparatedList(responseType)));

            return Parameter(Identifier("callback"))
                .WithType(paramType);
        }

        private static TypeSyntax GetMessageResponseType(Schema schema)
        {
            return schema.Tag == Schema.Type.Null
                ? PredefinedType(Token(SyntaxKind.VoidKeyword))
                : AvroSchemaUtilities.GetFieldType(schema);
        }
    }
}
