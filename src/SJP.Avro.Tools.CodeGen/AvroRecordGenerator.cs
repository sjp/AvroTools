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
/// Generates C# class files for Avro record or error types.
/// </summary>
public class AvroRecordGenerator : ICodeGenerator<RecordSchema>
{
    private const string FieldPosParameterName = "fieldPos";
    private const string FieldValueParameterName = "fieldValue";

    /// <summary>
    /// Creates a C# implementation of an Avro record or error type.
    /// </summary>
    /// <param name="schema">A definition of a record/error type in Avro schema.</param>
    /// <param name="baseNamespace">The base namespace to use (when one is absent).</param>
    /// <param name="options">Optional C# output style options. Defaults to <see cref="CodeGenOptions.Default"/> when omitted.</param>
    /// <returns>A string representing a C# file containing a class definition.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="schema"/> or <paramref name="baseNamespace"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="baseNamespace"/> is empty or whitespace and <paramref name="schema"/> does not declare a namespace.</exception>
    /// <exception cref="NotSupportedException"><paramref name="schema"/> is an error type named after a member its generated class is obliged to declare.</exception>
    public string Generate(RecordSchema schema, string baseNamespace, CodeGenOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(baseNamespace);

        options ??= CodeGenOptions.Default;

        var isError = schema.Tag == Schema.Type.Error;
        var typeName = schema.Name;

        if (isError && UnavoidableErrorMemberNames.Contains(typeName))
        {
            throw new NotSupportedException(
                $"The error type '{schema.Fullname}' cannot be generated. An error is generated as a class deriving from "
                + $"{typeof(SpecificException).FullName}, which obliges it to declare members named "
                + $"{AvroSchemaUtilities.SchemaMemberName}, {nameof(ISpecificRecord.Get)} and {nameof(ISpecificRecord.Put)}, "
                + "and a C# type may not declare a member of its own name. Rename the type in the schema.");
        }

        var ns = SyntaxUtilities.ResolveNamespace(schema.Namespace, baseNamespace, schema.Fullname);

        var namespaceDeclaration = NamespaceDeclaration(SyntaxUtilities.SafeNamespaceName(ns));

        // Every name the generated type carries is settled before anything is emitted, because each
        // one narrows what the next may be called.
        var schemaFieldName = ReservedNames.MakeAvailable(
            AvroSchemaUtilities.SchemaFieldName,
            new HashSet<string>(StringComparer.Ordinal) { typeName });
        var fieldEnumName = GetFieldEnumName(schema, typeName, schemaFieldName);
        var propertyNames = BuildPropertyNames(schema, isError, typeName, schemaFieldName, fieldEnumName);
        var backingFieldNames = BuildBackingFieldNames(schema, typeName, schemaFieldName, fieldEnumName, propertyNames);
        var localEnumName = GetLocalFieldEnumName(schema, typeName, schemaFieldName, fieldEnumName, propertyNames, backingFieldNames);

        var schemaField = AvroSchemaUtilities.CreateSchemaDefinition(AvroSchemaUtilities.ToPortableJson(schema.ToString()), schemaFieldName);
        var schemaProperty = AvroSchemaUtilities.CreateSchemaProperty(schemaFieldName);

        if (isError)
        {
            schemaProperty = schemaProperty
                 .WithModifiers(
                     TokenList(
                         Token(SyntaxKind.PublicKeyword),
                         Token(SyntaxKind.OverrideKeyword)));
        }
        else if (string.Equals(typeName, AvroSchemaUtilities.SchemaMemberName, StringComparison.Ordinal))
        {
            schemaProperty = SyntaxUtilities.AsExplicitImplementation(schemaProperty, typeof(ISpecificRecord));
        }

        var properties = schema.Fields
            .SelectMany(c => BuildField(c, propertyNames[c.Name], backingFieldNames[c.Name], ns, options));

        var getMethod = GenerateGetMethod(schema, propertyNames, fieldEnumName, localEnumName);
        var putMethod = GeneratePutMethod(schema, propertyNames, backingFieldNames, fieldEnumName, localEnumName, ns, options);

        if (isError)
        {
            var overrideModifiers = TokenList(
                Token(SyntaxKind.PublicKeyword),
                Token(SyntaxKind.OverrideKeyword));

            getMethod = getMethod.WithModifiers(overrideModifiers);
            putMethod = putMethod.WithModifiers(overrideModifiers);
        }
        else
        {
            if (string.Equals(typeName, nameof(ISpecificRecord.Get), StringComparison.Ordinal))
                getMethod = SyntaxUtilities.AsExplicitImplementation(getMethod, typeof(ISpecificRecord));
            if (string.Equals(typeName, nameof(ISpecificRecord.Put), StringComparison.Ordinal))
                putMethod = SyntaxUtilities.AsExplicitImplementation(putMethod, typeof(ISpecificRecord));
        }

        var enumDecl = GenerateFieldMappingEnum(schema, fieldEnumName);

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

        var baseType = SyntaxUtilities.GlobalName(isError ? typeof(SpecificException) : typeof(ISpecificRecord));

        TypeDeclarationSyntax generatedType = isError
            ? ClassDeclaration(SyntaxUtilities.SafeIdentifier(typeName))
            : RecordDeclaration(Token(SyntaxKind.RecordKeyword), SyntaxUtilities.SafeIdentifier(typeName));

        generatedType = generatedType
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
            .WithMembers(List(members));

        // The base list and brace tokens widen the static type, hence the cast back.
        generatedType = (TypeDeclarationSyntax)generatedType
            .AddBaseListTypes(SimpleBaseType(baseType))
            .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
            .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken));

        generatedType = generatedType
            .WithLeadingTrivia(SyntaxUtilities.BuildCommentTrivia(schema.Documentation));

        var document = CompilationUnit()
            .WithMembers(
                SingletonList<MemberDeclarationSyntax>(
                    namespaceDeclaration
                        .WithMembers(
                            SingletonList<MemberDeclarationSyntax>(generatedType))));

        return SyntaxUtilities.Format(document);
    }

    private static IEnumerable<MemberDeclarationSyntax> BuildField(Field field, string propertyName, string backingFieldName, string containingNamespace, CodeGenOptions options)
    {
        var fieldIsNullable = AvroSchemaUtilities.IsNullable(field.Schema);

        if (!SyntaxUtilities.TypeSyntaxMap.TryGetValue(field.Schema.Tag, out var columnTypeSyntax))
        {
            columnTypeSyntax = AvroSchemaUtilities.GetFieldType(field.Schema, containingNamespace);
        }

        var isNotNullRefType = !fieldIsNullable && !AvroSchemaUtilities.IsValueType(field.Schema);
        var isRequired = options.RequiredProperties && !fieldIsNullable && field.DefaultValue == null;

        var modifiers = isRequired
            ? TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.RequiredKeyword))
            : TokenList(Token(SyntaxKind.PublicKeyword));

        var baseProperty = PropertyDeclaration(
            columnTypeSyntax,
            SyntaxUtilities.SafeIdentifier(propertyName)
        );

        if (options.InitOnlyProperties)
        {
            // ISpecificRecord.Put mutates fields after construction, which is incompatible with a
            // compiler-enforced init accessor from a regular method. Route the init accessor
            // through a private backing field so Put can still assign it directly.
            var backingFieldDeclarator = VariableDeclarator(Identifier(backingFieldName));
            if (isNotNullRefType)
                backingFieldDeclarator = backingFieldDeclarator.WithInitializer(SyntaxUtilities.NotNullDefault);

            var backingField = FieldDeclaration(
                    VariableDeclaration(columnTypeSyntax)
                        .WithVariables(SingletonSeparatedList(backingFieldDeclarator)))
                .WithModifiers(TokenList(Token(SyntaxKind.PrivateKeyword)))
                .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

            var accessorList = AccessorList(
                List(
                [
                    AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithExpressionBody(ArrowExpressionClause(IdentifierName(backingFieldName)))
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
                    AccessorDeclaration(SyntaxKind.InitAccessorDeclaration)
                        .WithExpressionBody(
                            ArrowExpressionClause(
                                AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    IdentifierName(backingFieldName),
                                    IdentifierName("value"))))
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
                ]));

            var initProperty = baseProperty
                .WithModifiers(modifiers)
                .WithAccessorList(accessorList)
                .WithTrailingTrivia(TriviaList(CarriageReturnLineFeed, CarriageReturnLineFeed));

            initProperty = initProperty
                .WithLeadingTrivia(SyntaxUtilities.BuildCommentTrivia(field.Documentation));

            yield return backingField;
            yield return initProperty;
            yield break;
        }

        var columnSyntax = baseProperty
            .WithModifiers(modifiers)
            .WithAccessorList(SyntaxUtilities.PropertyGetSetDeclaration)
            .WithTrailingTrivia(TriviaList(CarriageReturnLineFeed, CarriageReturnLineFeed));

        columnSyntax = columnSyntax
            .WithLeadingTrivia(SyntaxUtilities.BuildCommentTrivia(field.Documentation));

        if (!isNotNullRefType || isRequired)
        {
            yield return columnSyntax;
            yield break;
        }

        yield return columnSyntax
            .WithInitializer(SyntaxUtilities.NotNullDefault)
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
            .WithTrailingTrivia(TriviaList(CarriageReturnLineFeed, CarriageReturnLineFeed));
    }

    /// <summary>
    /// The members <c>SpecificException</c> declares as abstract. A generated error type is a class
    /// deriving from it, so it has to override them under exactly these names; an error named after
    /// one of them therefore cannot be generated at all.
    /// </summary>
    private static readonly IReadOnlySet<string> UnavoidableErrorMemberNames = new HashSet<string>(StringComparer.Ordinal)
    {
        AvroSchemaUtilities.SchemaMemberName,
        nameof(ISpecificRecord.Get),
        nameof(ISpecificRecord.Put)
    };

    /// <summary>
    /// Computes the C# property name for each Avro field. A member may not share its name with the
    /// type that declares it, nor with the other members generated alongside the properties, nor
    /// with the parameters of <c>Get</c>/<c>Put</c> (which would otherwise shadow it inside those
    /// method bodies, so that reads returned the index and writes assigned the parameter to
    /// itself), nor with a member the generated type inherits or the compiler writes into it, so a
    /// field with one of those names gets an underscore-suffixed property. The Avro name is
    /// unaffected: it stays in the schema, in the field position enum and in the <c>Get</c>/
    /// <c>Put</c> switches.
    /// </summary>
    private static IReadOnlyDictionary<string, string> BuildPropertyNames(
        RecordSchema recordSchema,
        bool isError,
        string typeName,
        string schemaFieldName,
        string fieldEnumName)
    {
        var unavailableNames = new HashSet<string>(StringComparer.Ordinal)
        {
            typeName,
            fieldEnumName,
            schemaFieldName,
            AvroSchemaUtilities.SchemaMemberName,
            nameof(ISpecificRecord.Get),
            nameof(ISpecificRecord.Put),
            FieldPosParameterName,
            FieldValueParameterName
        };

        unavailableNames.UnionWith(ReservedNames.ObjectMembers);

        // An error is generated as a class deriving from SpecificException; every other record is
        // generated as a C# record, which the compiler fills out with members of its own.
        unavailableNames.UnionWith(isError ? ReservedNames.ExceptionMembers : ReservedNames.RecordMembers);

        var propertyNames = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var field in recordSchema.Fields)
        {
            var candidate = ReservedNames.MakeAvailable(field.Name, unavailableNames);

            propertyNames[field.Name] = candidate;
            unavailableNames.Add(candidate);
        }

        return propertyNames;
    }

    /// <summary>
    /// Computes a unique backing field name per field, avoiding collisions with the generated
    /// property names, the type's own name, the field holding the parsed schema, the field position
    /// enum, and backing field names already claimed by other fields in this same record.
    /// </summary>
    private static IReadOnlyDictionary<string, string> BuildBackingFieldNames(
        RecordSchema recordSchema,
        string typeName,
        string schemaFieldName,
        string fieldEnumName,
        IReadOnlyDictionary<string, string> propertyNames)
    {
        var reservedNames = new HashSet<string>(propertyNames.Values, StringComparer.Ordinal)
        {
            typeName,
            schemaFieldName,
            fieldEnumName
        };
        var backingFieldNames = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var field in recordSchema.Fields)
        {
            var candidate = "_" + field.Name;
            while (reservedNames.Contains(candidate))
                candidate = "_" + candidate;

            backingFieldNames[field.Name] = candidate;
            reservedNames.Add(candidate);
        }

        return backingFieldNames;
    }

    private static MethodDeclarationSyntax GenerateGetMethod(RecordSchema recordSchema, IReadOnlyDictionary<string, string> propertyNames, string enumName, string localEnumVarName)
    {
        var parameterList = ParameterList(
            SingletonSeparatedList(
                Parameter(
                    Identifier(FieldPosParameterName))
                .WithType(
                    PredefinedType(
                        Token(SyntaxKind.IntKeyword)))));

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
                                IdentifierName(FieldPosParameterName)))))));

        var fieldCaseStatements = recordSchema
            .Fields
            .Select(f => GenerateGetCaseStatement(f, enumName, propertyNames[f.Name]))
            .Concat([GenerateGetDefaultCaseStatement()])
            .ToList();

        return MethodDeclaration(
                PredefinedType(
                    Token(SyntaxKind.ObjectKeyword)),
                Identifier(nameof(ISpecificRecord.Get)))
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
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

    private static MethodDeclarationSyntax GeneratePutMethod(RecordSchema recordSchema, IReadOnlyDictionary<string, string> propertyNames, IReadOnlyDictionary<string, string> backingFieldNames, string enumName, string localEnumVarName, string containingNamespace, CodeGenOptions options)
    {
        var parameterList = ParameterList(
            SeparatedList<ParameterSyntax>(
                new SyntaxNodeOrToken[]
                {
                        Parameter(
                            Identifier(FieldPosParameterName))
                            .WithType(
                                PredefinedType(
                                    Token(SyntaxKind.IntKeyword))),
                        Token(SyntaxKind.CommaToken),
                        Parameter(
                            Identifier(FieldValueParameterName))
                            .WithType(
                                PredefinedType(
                                    Token(SyntaxKind.ObjectKeyword)))
                }));

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
                                IdentifierName(FieldPosParameterName)))))));

        var fieldCaseStatements = recordSchema
            .Fields
            .Select(f => GeneratePutCaseStatement(f, enumName, propertyNames[f.Name], backingFieldNames[f.Name], containingNamespace, options))
            .Concat([GeneratePutDefaultCaseStatement()])
            .ToList();

        return MethodDeclaration(
                PredefinedType(
                    Token(SyntaxKind.VoidKeyword)),
                Identifier(nameof(ISpecificRecord.Put)))
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
            .WithParameterList(parameterList)
            .WithBody(
                Block(
                    intToEnumAssignment,
                    SwitchStatement(
                        IdentifierName(localEnumVarName))
                    .WithSections(
                        List(fieldCaseStatements))));
    }

    private static SwitchExpressionArmSyntax GenerateGetCaseStatement(Field field, string enumClassName, string propertyName)
    {
        // A decimal property must be converted back to the AvroDecimal the writer expects.
        var decimalSchema = AvroSchemaUtilities.GetConvertedDecimalSchema(field.Schema);
        var valueExpression = decimalSchema != null
            ? GenerateGetDecimalCase(propertyName, AvroSchemaUtilities.GetDecimalScale(decimalSchema), AvroSchemaUtilities.IsNullable(field.Schema))
            : SyntaxUtilities.SafeIdentifierName(propertyName);

        return SwitchExpressionArm(
            ConstantPattern(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName(enumClassName),
                    SyntaxUtilities.SafeIdentifierName(field.Name))),
            valueExpression);
    }

    private static ExpressionSyntax GenerateGetDecimalCase(string propertyName, int scale, bool isNullable)
    {
        // Only the non-null branch can be rounded, so a nullable decimal keeps its null as-is.
        var value = isNullable
            ? MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxUtilities.SafeIdentifierName(propertyName),
                IdentifierName(nameof(Nullable<int>.Value)))
            : (ExpressionSyntax)SyntaxUtilities.SafeIdentifierName(propertyName);

        var conversion = GenerateAvroDecimalCreation(value, scale);

        if (!isNullable)
            return conversion;

        return ConditionalExpression(
            BinaryExpression(
                SyntaxKind.EqualsExpression,
                SyntaxUtilities.SafeIdentifierName(propertyName),
                LiteralExpression(SyntaxKind.NullLiteralExpression)),
            CastExpression(
                NullableType(AvroSchemaUtilities.AvroDecimalType),
                LiteralExpression(SyntaxKind.NullLiteralExpression)),
            conversion);
    }

    private static ObjectCreationExpressionSyntax GenerateAvroDecimalCreation(ExpressionSyntax value, int scale)
    {
        return ObjectCreationExpression(
            AvroSchemaUtilities.AvroDecimalType)
            .WithArgumentList(
                ArgumentList(
                    SingletonSeparatedList(
                        Argument(
                            BinaryExpression(
                                SyntaxKind.AddExpression,
                                InvocationExpression(
                                    MemberAccessExpression(
                                        SyntaxKind.SimpleMemberAccessExpression,
                                        SyntaxUtilities.GlobalName(typeof(Math)),
                                        IdentifierName(nameof(Math.Round))))
                                .WithArgumentList(
                                    ArgumentList(
                                        SeparatedList<ArgumentSyntax>(
                                            new SyntaxNodeOrToken[]
                                            {
                                                    Argument(value),
                                                    Token(SyntaxKind.CommaToken),
                                                    Argument(
                                                        LiteralExpression(
                                                            SyntaxKind.NumericLiteralExpression,
                                                            Literal(scale))),
                                                    Token(SyntaxKind.CommaToken),
                                                    Argument(
                                                        MemberAccessExpression(
                                                            SyntaxKind.SimpleMemberAccessExpression,
                                                            SyntaxUtilities.GlobalName(typeof(MidpointRounding)),
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
                    SyntaxUtilities.GlobalName(typeof(AvroRuntimeException)))
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
                                        IdentifierName(FieldPosParameterName)),
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
                            SyntaxUtilities.GlobalName(typeof(AvroRuntimeException)))
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
                                                IdentifierName(FieldPosParameterName)),
                                            LiteralExpression(
                                                SyntaxKind.StringLiteralExpression,
                                                Literal($" in {nameof(ISpecificRecord.Put)}()"))))))))));
    }

    private static SwitchSectionSyntax GeneratePutCaseStatement(Field field, string enumClassName, string propertyName, string backingFieldName, string containingNamespace, CodeGenOptions options)
    {
        // A decimal property must be converted from the AvroDecimal the reader supplies.
        if (AvroSchemaUtilities.GetConvertedDecimalSchema(field.Schema) != null)
        {
            return GenerateDecimalPutCaseStatement(field, enumClassName, propertyName, backingFieldName, options);
        }

        var fieldType = AvroSchemaUtilities.GetFieldType(field.Schema, containingNamespace);
        var assignmentTargetName = options.InitOnlyProperties ? backingFieldName : propertyName;

        return SwitchSection()
            .WithLabels(
                SingletonList<SwitchLabelSyntax>(
                    CaseSwitchLabel(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            IdentifierName(enumClassName),
                            SyntaxUtilities.SafeIdentifierName(field.Name)))))
            .WithStatements(
                List(
                    new StatementSyntax[]{
                            ExpressionStatement(
                                AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    SyntaxUtilities.SafeIdentifierName(assignmentTargetName),
                                    CastExpression(
                                        fieldType,
                                        IdentifierName(FieldValueParameterName)))),
                            BreakStatement()}));
    }

    private static SwitchSectionSyntax GenerateDecimalPutCaseStatement(Field field, string enumClassName, string propertyName, string backingFieldName, CodeGenOptions options)
    {
        var assignmentTargetName = options.InitOnlyProperties ? backingFieldName : propertyName;

        ExpressionSyntax conversion = InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    AvroSchemaUtilities.AvroDecimalType,
                    IdentifierName(nameof(AvroDecimal.ToDecimal))))
            .WithArgumentList(
                ArgumentList(
                    SingletonSeparatedList(
                        Argument(
                            CastExpression(
                                AvroSchemaUtilities.AvroDecimalType,
                                IdentifierName(FieldValueParameterName))))));

        // A nullable decimal arrives as null whenever the union's null branch was written.
        if (AvroSchemaUtilities.IsNullable(field.Schema))
        {
            conversion = ConditionalExpression(
                BinaryExpression(
                    SyntaxKind.EqualsExpression,
                    IdentifierName(FieldValueParameterName),
                    LiteralExpression(SyntaxKind.NullLiteralExpression)),
                CastExpression(
                    NullableType(PredefinedType(Token(SyntaxKind.DecimalKeyword))),
                    LiteralExpression(SyntaxKind.NullLiteralExpression)),
                conversion);
        }

        return SwitchSection()
            .WithLabels(
                SingletonList<SwitchLabelSyntax>(
                    CaseSwitchLabel(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            IdentifierName(enumClassName),
                            SyntaxUtilities.SafeIdentifierName(field.Name)))))
            .WithStatements(
                List(
                    new StatementSyntax[]
                    {
                            ExpressionStatement(
                                AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    SyntaxUtilities.SafeIdentifierName(assignmentTargetName),
                                    conversion)),
                            BreakStatement()
                    }));
    }

    private static EnumDeclarationSyntax GenerateFieldMappingEnum(RecordSchema recordSchema, string enumName)
    {
        var members = recordSchema.Fields
            .Select(f => f.Name)
            .Select(m => EnumMemberDeclaration(SyntaxUtilities.SafeIdentifier(m)))
            .ToList();

        return EnumDeclaration(enumName)
            .AddModifiers(Token(SyntaxKind.PrivateKeyword))
            .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
            .WithMembers(SeparatedList(members))
            .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken));
    }

    /// <summary>
    /// Names the nested enum that maps a field position onto a field. It is declared alongside the
    /// properties, so it has to differ from every name the record already spends.
    /// </summary>
    private static string GetFieldEnumName(RecordSchema recordSchema, string typeName, string schemaFieldName)
    {
        var candidate = char.ToUpperInvariant(typeName[0])
            + typeName[1..]
            + "Field";

        var reservedNames = new HashSet<string>(recordSchema.Fields.Select(static f => f.Name), StringComparer.Ordinal)
        {
            typeName,
            schemaFieldName
        };

        while (reservedNames.Contains(candidate))
            candidate = "_" + candidate;

        return candidate;
    }

    /// <summary>
    /// Names the local that <c>Get</c> and <c>Put</c> convert the field position into. A local
    /// shadows anything of the same name for the rest of the method body, so it has to differ from
    /// every member those bodies go on to read or assign.
    /// </summary>
    private static string GetLocalFieldEnumName(
        RecordSchema recordSchema,
        string typeName,
        string schemaFieldName,
        string fieldEnumName,
        IReadOnlyDictionary<string, string> propertyNames,
        IReadOnlyDictionary<string, string> backingFieldNames)
    {
        var candidate = char.ToLowerInvariant(typeName[0])
            + typeName[1..]
            + "Field";

        var reservedNames = new HashSet<string>(recordSchema.Fields.Select(static f => f.Name), StringComparer.Ordinal)
        {
            typeName,
            schemaFieldName,
            fieldEnumName,
            FieldPosParameterName,
            FieldValueParameterName
        };

        reservedNames.UnionWith(propertyNames.Values);
        reservedNames.UnionWith(backingFieldNames.Values);

        while (reservedNames.Contains(candidate))
            candidate = "_" + candidate;

        return candidate;
    }
}