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
/// Generates C# class files for Avro record or error types.
/// </summary>
public class AvroRecordGenerator : ICodeGenerator<RecordSchema>
{
    /// <summary>
    /// Creates a C# implementation of an Avro record or error type.
    /// </summary>
    /// <param name="schema">A definition of a record/error type in Avro schema.</param>
    /// <param name="baseNamespace">The base namespace to use (when one is absent).</param>
    /// <returns>A string representing a C# file containing a class definition.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="schema"/> or <paramref name="baseNamespace"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="baseNamespace"/> is empty or whitespace.</exception>
    public string Generate(RecordSchema schema, string baseNamespace)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseNamespace);

        var isError = schema.Tag == Schema.Type.Error;
        var ns = schema.Namespace ?? baseNamespace;

        var namespaceDeclaration = NamespaceDeclaration(ParseName(ns));

        var namespaces = GetRequiredNamespaces(schema);
        var usingStatements = namespaces
            .Select(static ns => ParseName(ns))
            .Select(UsingDirective)
            .ToList();

        var schemaField = AvroSchemaUtilities.CreateSchemaDefinition(schema.ToString());
        var schemaProperty = AvroSchemaUtilities.CreateSchemaProperty();

        if (isError)
        {
            schemaProperty = schemaProperty
                 .WithModifiers(
                     TokenList(
                         Token(SyntaxKind.PublicKeyword),
                         Token(SyntaxKind.OverrideKeyword)));
        }

        var properties = schema.Fields
            .ConvertAll(c => BuildField(c, schema.Name));

        var getMethod = GenerateGetMethod(schema);
        var putMethod = GeneratePutMethod(schema);
        var enumDecl = GenerateFieldMappingEnum(schema);

        var members = new MemberDeclarationSyntax[]
        {
                schemaField,
                schemaProperty
        }.Concat(properties)
        .Concat(
        [
                getMethod,
                putMethod,
                enumDecl
        ]);

        var baseType = isError ? nameof(SpecificException) : nameof(ISpecificRecord);

        var generatedRecord = RecordDeclaration(Token(SyntaxKind.RecordKeyword), schema.Name)
            .AddModifiers(Token(SyntaxKind.PublicKeyword))
            .AddBaseListTypes(SimpleBaseType(IdentifierName(baseType)))
            .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
            .WithMembers(List(members))
            .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken));

        if (schema.Documentation != null)
        {
            generatedRecord = generatedRecord
                .WithLeadingTrivia(SyntaxUtilities.BuildCommentTrivia(schema.Documentation));
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

    private static IEnumerable<string> GetRequiredNamespaces(RecordSchema record)
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

    private static PropertyDeclarationSyntax BuildField(Field field, string className)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);

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

    private static MethodDeclarationSyntax GenerateGetMethod(RecordSchema recordSchema)
    {
        var isError = recordSchema.Tag == Schema.Type.Error;

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
            .Concat([GenerateGetDefaultCaseStatement()])
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
                Identifier(nameof(ISpecificRecord.Get)))
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

    private static MethodDeclarationSyntax GeneratePutMethod(RecordSchema recordSchema)
    {
        var isError = recordSchema.Tag == Schema.Type.Error;

        var parameterList = ParameterList(
            SeparatedList<ParameterSyntax>(
                new SyntaxNodeOrToken[]
                {
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
                                    Token(SyntaxKind.ObjectKeyword)))
                }));

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
            .Concat([GeneratePutDefaultCaseStatement()])
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
                Identifier(nameof(ISpecificRecord.Put)))
            .WithModifiers(modifiers)
            .WithParameterList(parameterList)
            .WithBody(
                Block(
                    intToEnumAssignment,
                    SwitchStatement(
                        IdentifierName(localEnumVarName))
                    .WithSections(
                        List(fieldCaseStatements))));
    }

    private static SwitchExpressionArmSyntax GenerateGetCaseStatement(Field field, string enumClassName)
    {
        // special case for decimal
        if (field.Schema is LogicalSchema logicalSchema && logicalSchema.LogicalTypeName == "decimal")
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

    private static ObjectCreationExpressionSyntax GenerateGetDecimalCase(Field field, int scale)
    {
        return ObjectCreationExpression(
            IdentifierName(nameof(AvroDecimal)))
            .WithArgumentList(
                ArgumentList(
                    SingletonSeparatedList(
                        Argument(
                            BinaryExpression(
                                SyntaxKind.AddExpression,
                                InvocationExpression(
                                    MemberAccessExpression(
                                        SyntaxKind.SimpleMemberAccessExpression,
                                        IdentifierName(nameof(Math)),
                                        IdentifierName(nameof(Math.Round))))
                                .WithArgumentList(
                                    ArgumentList(
                                        SeparatedList<ArgumentSyntax>(
                                            new SyntaxNodeOrToken[]
                                            {
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
                                                            IdentifierName(nameof(MidpointRounding)),
                                                            IdentifierName(nameof(MidpointRounding.AwayFromZero))))
                                            }))),
                                ObjectCreationExpression(
                                    PredefinedType(
                                        Token(SyntaxKind.DecimalKeyword)))
                                .WithArgumentList(
                                    ArgumentList(
                                        SeparatedList<ArgumentSyntax>(
                                            new SyntaxNodeOrToken[]
                                            {
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
                                                            Literal(scale)))
                                            }))))))));
    }

    private static SwitchExpressionArmSyntax GenerateGetDefaultCaseStatement()
    {
        return SwitchExpressionArm(
            DiscardPattern(),
            ThrowExpression(
                ObjectCreationExpression(
                    IdentifierName(nameof(AvroRuntimeException)))
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
                                        Literal($" in {nameof(ISpecificRecord.Get)}()")))))))));
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
                            IdentifierName(nameof(AvroRuntimeException)))
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
                                                Literal($" in {nameof(ISpecificRecord.Put)}()"))))))))));
    }

    private static SwitchSectionSyntax GeneratePutCaseStatement(Field field, string enumClassName)
    {
        // special case for decimal
        if (field.Schema is LogicalSchema logicalSchema && logicalSchema.LogicalTypeName == "decimal")
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

    private static SwitchSectionSyntax GenerateDecimalPutCaseStatement(Field field, string enumClassName)
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
                                            IdentifierName(nameof(AvroDecimal)),
                                            IdentifierName(nameof(AvroDecimal.ToDecimal))))
                                    .WithArgumentList(
                                        ArgumentList(
                                            SingletonSeparatedList(
                                                Argument(
                                                    CastExpression(
                                                        IdentifierName(nameof(AvroDecimal)),
                                                        IdentifierName("fieldValue")))))))),
                            BreakStatement()
                    }));
    }

    private static EnumDeclarationSyntax GenerateFieldMappingEnum(RecordSchema recordSchema)
    {
        var members = recordSchema.Fields
            .Select(f => f.Name)
            .Select(m => EnumMemberDeclaration(m))
            .ToList();

        var enumName = GetFieldEnumName(recordSchema);

        return EnumDeclaration(enumName)
            .AddModifiers(Token(SyntaxKind.PrivateKeyword))
            .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
            .WithMembers(SeparatedList(members))
            .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken));
    }

    private static string GetFieldEnumName(RecordSchema recordSchema)
    {
        var candidate = char.ToUpper(recordSchema.Name[0])
            + recordSchema.Name[1..]
            + "Field";

        while (recordSchema.Fields.Any(f => f.Name == candidate))
            candidate = "_" + candidate;

        return candidate;
    }

    private static string GetLocalFieldEnumName(RecordSchema recordSchema)
    {
        var candidate = char.ToLower(recordSchema.Name[0])
            + recordSchema.Name[1..]
            + "Field";

        while (recordSchema.Fields.Any(f => f.Name == candidate))
            candidate = "_" + candidate;

        return candidate;
    }
}