using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using AvroSchema = Avro.Schema;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;


namespace SJP.Avro.Tools.CodeGen
{
    public class AvroRecordGenerator
    {
        public string Generate(string json)
        {
            //var json = File.ReadAllText(filePath);
            //var schema = AvroSchema.Parse(json);

            var p = global::Avro.Protocol.Parse(json);
            var schema = p.Types.First(t => t is global::Avro.RecordSchema);

            var recordSchema = schema as global::Avro.RecordSchema;
            var isError = recordSchema.Tag == AvroSchema.Type.Error;
            var ns = recordSchema.Namespace;

            var namespaceDeclaration = NamespaceDeclaration(ParseName(ns ?? "FakeExample"));

            var namespaces = GetRequiredNamespaces(recordSchema);
            var usingStatements = namespaces
                .Select(static ns => ParseName(ns))
                .Select(UsingDirective)
                .ToList();

            var schemaField = AvroSchemaUtilities.CreateSchemaDefinition(recordSchema.ToString());
            var schemaProperty = AvroSchemaUtilities.CreateSchemaProperty();

            if (isError)
            {
                schemaProperty = schemaProperty
                     .WithModifiers(
                         TokenList(
                             Token(SyntaxKind.PublicKeyword),
                             Token(SyntaxKind.OverrideKeyword)));
            }

            var properties = recordSchema.Fields
                .ConvertAll(c => BuildField(c, recordSchema.Name));

            var getMethod = GenerateGetMethod(recordSchema);
            var putMethod = GeneratePutMethod(recordSchema);
            var enumDecl = GenerateFieldMappingEnum(recordSchema);

            var members = new MemberDeclarationSyntax[]
            {
                schemaField,
                schemaProperty
            }.Concat(properties)
            .Concat(new MemberDeclarationSyntax[]
            {
                getMethod,
                putMethod,
                enumDecl
            });

            var baseType = isError
                ? nameof(global::Avro.Specific.SpecificException)
                : nameof(global::Avro.Specific.ISpecificRecord);

            var generatedRecord = RecordDeclaration(Token(SyntaxKind.RecordKeyword), recordSchema.Name)
                .AddModifiers(Token(SyntaxKind.PublicKeyword))
                .AddBaseListTypes(SimpleBaseType(IdentifierName(baseType)))
                .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
                .WithMembers(List(members))
                .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken));

            if (recordSchema.Documentation != null)
            {
                generatedRecord = generatedRecord
                    .WithLeadingTrivia(SyntaxUtilities.BuildCommentTrivia(recordSchema.Documentation));
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

        private static IEnumerable<string> GetRequiredNamespaces(global::Avro.RecordSchema record)
        {
            var systemNamespaces = new[]
            {
                "System",
                "System.Collections.Generic"
            };

            var avroNamespaces = new[]
            {
                "Avro",
                "Avro.Specific"
            };

            var namespaces = new HashSet<string>(systemNamespaces.Concat(avroNamespaces));

            var baseNamespace = record.Namespace;

            var scannedNamespaces = record.Fields
                .Select(f => f.Schema)
                .SelectMany(GetNamespacesForType)
                .Where(ns => ns != baseNamespace);
            foreach (var ns in scannedNamespaces)
                namespaces.Add(ns);

            return namespaces.OrderNamespaces();
        }

        private static IEnumerable<string> GetNamespacesForType(global::Avro.Schema schema)
        {
            return schema switch
            {
                global::Avro.ArraySchema arraySchema => GetNamespacesForType(arraySchema.ItemSchema),
                global::Avro.MapSchema mapSchema => GetNamespacesForType(mapSchema.ValueSchema),
                global::Avro.UnionSchema unionSchema => unionSchema.Schemas.SelectMany(GetNamespacesForType),
                global::Avro.NamedSchema namedSchema => namedSchema.Namespace != null ? new[] { namedSchema.Namespace } : Array.Empty<string>(),
                _ => Array.Empty<string>()
            };
        }

        private static PropertyDeclarationSyntax BuildField(global::Avro.Field field, string className)
        {
            if (string.IsNullOrWhiteSpace(className))
                throw new ArgumentNullException(nameof(className));

            var fieldIsNullable = AvroSchemaUtilities.IsNullableRefType(field.Schema) || AvroSchemaUtilities.IsNullableValueType(field.Schema);

            if (!SyntaxUtilities.TypeSyntaxMap.TryGetValue(field.Schema.Tag, out var columnTypeSyntax))
            {
                columnTypeSyntax = AvroSchemaUtilities.GetFieldType(field.Schema);
            }

            var baseProperty = PropertyDeclaration(
                columnTypeSyntax,
                Identifier(field.Name)
            );

            var columnSyntax = baseProperty
                .WithModifiers(SyntaxTokenList.Create(Token(SyntaxKind.PublicKeyword)))
                .WithAccessorList(SyntaxUtilities.PropertyGetSetDeclaration)
                .WithTrailingTrivia(TriviaList(CarriageReturnLineFeed, CarriageReturnLineFeed));

            if (field.Documentation != null)
            {
                columnSyntax = columnSyntax
                    .WithLeadingTrivia(SyntaxUtilities.BuildCommentTrivia(field.Documentation));
            }

            var isNotNullRefType = !fieldIsNullable && !AvroSchemaUtilities.IsValueType(field.Schema);
            if (!isNotNullRefType)
                return columnSyntax;

            return columnSyntax
                .WithInitializer(SyntaxUtilities.NotNullDefault)
                .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
                .WithTrailingTrivia(TriviaList(CarriageReturnLineFeed, CarriageReturnLineFeed));
        }

        private static MethodDeclarationSyntax GenerateGetMethod(global::Avro.RecordSchema recordSchema)
        {
            var isError = recordSchema.Tag == AvroSchema.Type.Error;

            var parameterList = ParameterList(
                SingletonSeparatedList(
                    Parameter(
                        Identifier("fieldPos"))
                    .WithType(
                        PredefinedType(
                            Token(SyntaxKind.IntKeyword)))));

            var enumName = GetFieldEnumName(recordSchema);
            var localEnumVarName = GetLocalFieldEnumName(recordSchema);

            var intToEnumAssignment = LocalDeclarationStatement(
                VariableDeclaration(
                    IdentifierName(
                        Identifier(
                            TriviaList(),
                            SyntaxKind.VarKeyword,
                            "var",
                            "var",
                            TriviaList())))
                .WithVariables(
                    SingletonSeparatedList(
                        VariableDeclarator(
                            Identifier(localEnumVarName))
                        .WithInitializer(
                            EqualsValueClause(
                                CastExpression(
                                    IdentifierName(enumName),
                                    IdentifierName("fieldPos")))))));

            var fieldCaseStatements = recordSchema
                .Fields
                .Select(f => GenerateGetCaseStatement(f, enumName))
                .Concat(new[] { GenerateGetDefaultCaseStatement() })
                .ToList();

            var modifiers = isError
                ? TokenList(
                        Token(SyntaxKind.PublicKeyword),
                        Token(SyntaxKind.OverrideKeyword))
                : TokenList(
                        Token(SyntaxKind.PublicKeyword));

            return MethodDeclaration(
                    PredefinedType(
                        Token(SyntaxKind.ObjectKeyword)),
                    Identifier("Get"))
                .WithModifiers(modifiers)
                .WithParameterList(parameterList)
                .WithBody(
                    Block(
                        intToEnumAssignment,
                        ReturnStatement(
                            SwitchExpression(
                                IdentifierName(localEnumVarName))
                            .WithArms(
                                SeparatedList(fieldCaseStatements)))));
        }

        private static MethodDeclarationSyntax GeneratePutMethod(global::Avro.RecordSchema recordSchema)
        {
            var isError = recordSchema.Tag == AvroSchema.Type.Error;

            var parameterList = ParameterList(
                SeparatedList<ParameterSyntax>(
                    new SyntaxNodeOrToken[]{
                        Parameter(
                            Identifier("fieldPos"))
                        .WithType(
                            PredefinedType(
                                Token(SyntaxKind.IntKeyword))),
                        Token(SyntaxKind.CommaToken),
                        Parameter(
                            Identifier("fieldValue"))
                        .WithType(
                            PredefinedType(
                                Token(SyntaxKind.ObjectKeyword)))}));

            var enumName = GetFieldEnumName(recordSchema);
            var localEnumVarName = GetLocalFieldEnumName(recordSchema);

            var intToEnumAssignment = LocalDeclarationStatement(
                VariableDeclaration(
                    IdentifierName(
                        Identifier(
                            TriviaList(),
                            SyntaxKind.VarKeyword,
                            "var",
                            "var",
                            TriviaList())))
                .WithVariables(
                    SingletonSeparatedList(
                        VariableDeclarator(
                            Identifier(localEnumVarName))
                        .WithInitializer(
                            EqualsValueClause(
                                CastExpression(
                                    IdentifierName(enumName),
                                    IdentifierName("fieldPos")))))));

            var fieldCaseStatements = recordSchema
                .Fields
                .Select(f => GeneratePutCaseStatement(f, enumName))
                .Concat(new[] { GeneratePutDefaultCaseStatement() })
                .ToList();

            var modifiers = isError
                ? TokenList(
                        Token(SyntaxKind.PublicKeyword),
                        Token(SyntaxKind.OverrideKeyword))
                : TokenList(
                        Token(SyntaxKind.PublicKeyword));

            return MethodDeclaration(
                    PredefinedType(
                        Token(SyntaxKind.VoidKeyword)),
                    Identifier("Put"))
                .WithModifiers(modifiers)
                .WithParameterList(parameterList)
                .WithBody(
                    Block(
                        intToEnumAssignment,
                        SwitchStatement(
                            IdentifierName("transactionField"))
                        .WithSections(
                            List(fieldCaseStatements))));
        }

        private static SwitchExpressionArmSyntax GenerateGetCaseStatement(global::Avro.Field field, string enumClassName)
        {
            // special case for decimal
            if (field.Schema is global::Avro.LogicalSchema logicalSchema && logicalSchema.LogicalTypeName == "decimal")
            {
                var scale = byte.Parse(logicalSchema.GetProperty("scale"));
                var dec = GenerateGetDecimalCase(field, scale);

                return SwitchExpressionArm(
                    ConstantPattern(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            IdentifierName(enumClassName),
                            IdentifierName(field.Name))),
                    dec);
            }

            return SwitchExpressionArm(
                ConstantPattern(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName(enumClassName),
                        IdentifierName(field.Name))),
                IdentifierName(field.Name));
        }

        private static ObjectCreationExpressionSyntax GenerateGetDecimalCase(global::Avro.Field field, int scale)
        {
            return ObjectCreationExpression(
                IdentifierName("AvroDecimal"))
                .WithArgumentList(
                    ArgumentList(
                        SingletonSeparatedList(
                            Argument(
                                BinaryExpression(
                                    SyntaxKind.AddExpression,
                                    InvocationExpression(
                                        MemberAccessExpression(
                                            SyntaxKind.SimpleMemberAccessExpression,
                                            IdentifierName("Math"),
                                            IdentifierName("Round")))
                                    .WithArgumentList(
                                        ArgumentList(
                                            SeparatedList<ArgumentSyntax>(
                                                new SyntaxNodeOrToken[]{
                                                    Argument(
                                                        IdentifierName(field.Name)),
                                                    Token(SyntaxKind.CommaToken),
                                                    Argument(
                                                        LiteralExpression(
                                                            SyntaxKind.NumericLiteralExpression,
                                                            Literal(scale))),
                                                    Token(SyntaxKind.CommaToken),
                                                    Argument(
                                                        MemberAccessExpression(
                                                            SyntaxKind.SimpleMemberAccessExpression,
                                                            IdentifierName("MidpointRounding"),
                                                            IdentifierName("AwayFromZero")))}))),
                                    ObjectCreationExpression(
                                        PredefinedType(
                                            Token(SyntaxKind.DecimalKeyword)))
                                    .WithArgumentList(
                                        ArgumentList(
                                            SeparatedList<ArgumentSyntax>(
                                                new SyntaxNodeOrToken[]{
                                                    Argument(
                                                        LiteralExpression(
                                                            SyntaxKind.NumericLiteralExpression,
                                                            Literal(0))),
                                                    Token(SyntaxKind.CommaToken),
                                                    Argument(
                                                        LiteralExpression(
                                                            SyntaxKind.NumericLiteralExpression,
                                                            Literal(0))),
                                                    Token(SyntaxKind.CommaToken),
                                                    Argument(
                                                        LiteralExpression(
                                                            SyntaxKind.NumericLiteralExpression,
                                                            Literal(0))),
                                                    Token(SyntaxKind.CommaToken),
                                                    Argument(
                                                        LiteralExpression(
                                                            SyntaxKind.FalseLiteralExpression)),
                                                    Token(SyntaxKind.CommaToken),
                                                    Argument(
                                                        LiteralExpression(
                                                            SyntaxKind.NumericLiteralExpression,
                                                            Literal(scale)))}))))))));
        }

        private static SwitchExpressionArmSyntax GenerateGetDefaultCaseStatement()
        {
            return SwitchExpressionArm(
                DiscardPattern(),
                ThrowExpression(
                    ObjectCreationExpression(
                        IdentifierName("AvroRuntimeException"))
                    .WithArgumentList(
                        ArgumentList(
                            SingletonSeparatedList(
                                Argument(
                                    BinaryExpression(
                                        SyntaxKind.AddExpression,
                                        BinaryExpression(
                                            SyntaxKind.AddExpression,
                                            LiteralExpression(
                                                SyntaxKind.StringLiteralExpression,
                                                Literal("Bad index ")),
                                            IdentifierName("fieldPos")),
                                        LiteralExpression(
                                            SyntaxKind.StringLiteralExpression,
                                            Literal(" in Get()")))))))));
        }

        private static SwitchSectionSyntax GeneratePutDefaultCaseStatement()
        {
            return SwitchSection()
                .WithLabels(
                    SingletonList<SwitchLabelSyntax>(
                        DefaultSwitchLabel()))
                .WithStatements(
                    SingletonList<StatementSyntax>(
                        ThrowStatement(
                            ObjectCreationExpression(
                                IdentifierName("AvroRuntimeException"))
                            .WithArgumentList(
                                ArgumentList(
                                    SingletonSeparatedList(
                                        Argument(
                                            BinaryExpression(
                                                SyntaxKind.AddExpression,
                                                BinaryExpression(
                                                    SyntaxKind.AddExpression,
                                                    LiteralExpression(
                                                        SyntaxKind.StringLiteralExpression,
                                                        Literal("Bad index ")),
                                                    IdentifierName("fieldPos")),
                                                LiteralExpression(
                                                    SyntaxKind.StringLiteralExpression,
                                                    Literal(" in Put()"))))))))));
        }

        private static SwitchSectionSyntax GeneratePutCaseStatement(global::Avro.Field field, string enumClassName)
        {
            // special case for decimal
            if (field.Schema is global::Avro.LogicalSchema logicalSchema && logicalSchema.LogicalTypeName == "decimal")
            {
                return GenerateDecimalPutCaseStatement(field, enumClassName);
            }

            var fieldType = AvroSchemaUtilities.GetFieldType(field.Schema);

            return SwitchSection()
                .WithLabels(
                    SingletonList<SwitchLabelSyntax>(
                        CaseSwitchLabel(
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                IdentifierName(enumClassName),
                                IdentifierName(field.Name)))))
                .WithStatements(
                    List(
                        new StatementSyntax[]{
                            ExpressionStatement(
                                AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    IdentifierName(field.Name),
                                    CastExpression(
                                        fieldType,
                                        IdentifierName("fieldValue")))),
                            BreakStatement()}));
        }

        private static SwitchSectionSyntax GenerateDecimalPutCaseStatement(global::Avro.Field field, string enumClassName)
        {
            return SwitchSection()
                .WithLabels(
                    SingletonList<SwitchLabelSyntax>(
                        CaseSwitchLabel(
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                IdentifierName(enumClassName),
                                IdentifierName(field.Name)))))
                .WithStatements(
                    List(
                        new StatementSyntax[]
                        {
                            ExpressionStatement(
                                AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    IdentifierName(field.Name),
                                    InvocationExpression(
                                        MemberAccessExpression(
                                            SyntaxKind.SimpleMemberAccessExpression,
                                            IdentifierName("AvroDecimal"),
                                            IdentifierName("ToDecimal")))
                                    .WithArgumentList(
                                        ArgumentList(
                                            SingletonSeparatedList<ArgumentSyntax>(
                                                Argument(
                                                    CastExpression(
                                                        IdentifierName("AvroDecimal"),
                                                        IdentifierName("fieldValue")))))))),
                            BreakStatement()
                        }));
        }

        private static EnumDeclarationSyntax GenerateFieldMappingEnum(global::Avro.RecordSchema recordSchema)
        {
            var members = recordSchema.Fields
                .Select(f => f.Name)
                .Select(m => EnumMemberDeclaration(m))
                .ToList();

            var enumName = GetFieldEnumName(recordSchema);

            return EnumDeclaration(enumName)
                .AddModifiers(Token(SyntaxKind.PrivateKeyword))
                //.WithLeadingTrivia(BuildTableComment(enumSchema.Name, enumSchema.Documentation))
                .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
                .WithMembers(SeparatedList(members))
                .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken));
        }

        private static string GetFieldEnumName(global::Avro.RecordSchema recordSchema)
        {
            var candidate = char.ToUpper(recordSchema.Name[0])
                + recordSchema.Name[1..]
                + "Field";

            while (recordSchema.Fields.Any(f => f.Name == candidate))
                candidate = "_" + candidate;

            return candidate;
        }

        private static string GetLocalFieldEnumName(global::Avro.RecordSchema recordSchema)
        {
            var candidate = char.ToLower(recordSchema.Name[0])
                + recordSchema.Name[1..]
                + "Field";

            while (recordSchema.Fields.Any(f => f.Name == candidate))
                candidate = "_" + candidate;

            return candidate;
        }
    }
}
