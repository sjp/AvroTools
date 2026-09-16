using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
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

    // Every library type the generated code names is spelled out in full, and building that name
    // splits the namespace and checks each segment for a keyword. The names never vary, so each is
    // built once rather than per field or per record.
    private static readonly NameSyntax MathType = SyntaxUtilities.GlobalName(typeof(Math));
    private static readonly NameSyntax AvroTypeExceptionType = SyntaxUtilities.GlobalName(typeof(AvroTypeException));
    private static readonly NameSyntax AvroRuntimeExceptionType = SyntaxUtilities.GlobalName(typeof(AvroRuntimeException));
    private static readonly NameSyntax SpecificExceptionType = SyntaxUtilities.GlobalName(typeof(SpecificException));
    private static readonly NameSyntax SpecificRecordType = SyntaxUtilities.GlobalName(typeof(ISpecificRecord));

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

        // Typing a field, deciding whether it admits null and spotting a decimal that has to be
        // converted each walk the field's schema, and the property, the Get arm and the Put case all
        // ask the same questions of the same field. Each field is described once here and the
        // description handed to all three.
        var fields = schema.Fields
            .Select(f => FieldShape.Describe(f, propertyNames[f.Name], backingFieldNames[f.Name], ns))
            .ToList();

        var properties = fields.SelectMany(f => BuildField(f, options));

        var getMethod = GenerateGetMethod(fields, fieldEnumName, localEnumName);
        var putMethod = GeneratePutMethod(fields, fieldEnumName, localEnumName, options);

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

        // Both methods are one large switch over the fields, which is where formatting the
        // assembled tree spends nearly all of its time. Laying them out here leaves the formatter
        // only the surrounding declaration to work on.
        getMethod = SyntaxUtilities.WithConcreteWhitespace(getMethod);
        putMethod = SyntaxUtilities.WithConcreteWhitespace(putMethod);

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

        var baseType = isError ? SpecificExceptionType : SpecificRecordType;

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

        return SyntaxUtilities.GenerateDocument(namespaceDeclaration, generatedType);
    }

    /// <summary>
    /// Everything the generated members need to know about one field. Each fact costs a walk of the
    /// field's schema, and the property, the <c>Get</c> arm and the <c>Put</c> case each need the
    /// same ones, so a field is described once and the description shared. Sharing one
    /// <see cref="TypeSyntax"/> between the property and the cast in <c>Put</c> is safe: a syntax
    /// node is immutable, and placing one in a tree keeps the node it was built from.
    /// </summary>
    /// <param name="Name">The field's Avro name, as the schema and the field position enum spell it.</param>
    /// <param name="Documentation">The field's doc comment, or <c>null</c> when it has none.</param>
    /// <param name="HasDefaultValue">Whether the schema gives the field a default value.</param>
    /// <param name="PropertyName">The name of the C# property generated for the field.</param>
    /// <param name="BackingFieldName">The name of the private field behind an init-only property.</param>
    /// <param name="Type">The type the property is declared as, and that <c>Put</c> casts to.</param>
    /// <param name="IsNullable">Whether the field's position also admits a null value.</param>
    /// <param name="IsNotNullRefType">Whether the property is a reference type that cannot hold null.</param>
    /// <param name="ConvertedDecimalSchema">
    /// The decimal schema whose values cross the <c>ISpecificRecord</c> boundary as an
    /// <see cref="AvroDecimal"/>, or <c>null</c> when the field needs no conversion.
    /// </param>
    private sealed record FieldShape(
        string Name,
        string? Documentation,
        bool HasDefaultValue,
        string PropertyName,
        string BackingFieldName,
        TypeSyntax Type,
        bool IsNullable,
        bool IsNotNullRefType,
        LogicalSchema? ConvertedDecimalSchema)
    {
        public static FieldShape Describe(Field field, string propertyName, string backingFieldName, string containingNamespace)
        {
            var isNullable = AvroSchemaUtilities.IsNullable(field.Schema);

            return new FieldShape(
                field.Name,
                field.Documentation,
                field.DefaultValue != null,
                propertyName,
                backingFieldName,
                AvroSchemaUtilities.GetFieldType(field.Schema, containingNamespace),
                isNullable,
                !isNullable && !AvroSchemaUtilities.IsValueType(field.Schema),
                AvroSchemaUtilities.GetConvertedDecimalSchema(field.Schema));
        }
    }

    private static IEnumerable<MemberDeclarationSyntax> BuildField(FieldShape field, CodeGenOptions options)
    {
        var isRequired = options.RequiredProperties && !field.IsNullable && !field.HasDefaultValue;

        var modifiers = isRequired
            ? TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.RequiredKeyword))
            : TokenList(Token(SyntaxKind.PublicKeyword));

        var baseProperty = PropertyDeclaration(
            field.Type,
            SyntaxUtilities.SafeIdentifier(field.PropertyName)
        );

        if (options.InitOnlyProperties)
        {
            // ISpecificRecord.Put mutates fields after construction, which is incompatible with a
            // compiler-enforced init accessor from a regular method. Route the init accessor
            // through a private backing field so Put can still assign it directly.
            var backingFieldDeclarator = VariableDeclarator(Identifier(field.BackingFieldName));
            if (field.IsNotNullRefType)
                backingFieldDeclarator = backingFieldDeclarator.WithInitializer(SyntaxUtilities.NotNullDefault);

            var backingField = FieldDeclaration(
                    VariableDeclaration(field.Type)
                        .WithVariables(SingletonSeparatedList(backingFieldDeclarator)))
                .WithModifiers(TokenList(Token(SyntaxKind.PrivateKeyword)))
                .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

            var accessorList = AccessorList(
                List(
                [
                    AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithExpressionBody(ArrowExpressionClause(IdentifierName(field.BackingFieldName)))
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
                    AccessorDeclaration(SyntaxKind.InitAccessorDeclaration)
                        .WithExpressionBody(
                            ArrowExpressionClause(
                                AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    IdentifierName(field.BackingFieldName),
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

        if (!field.IsNotNullRefType || isRequired)
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
    }.ToFrozenSet();

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

    private static MethodDeclarationSyntax GenerateGetMethod(IReadOnlyList<FieldShape> fields, string enumName, string localEnumVarName)
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

        var fieldCaseStatements = fields
            .Select(f => GenerateGetCaseStatement(f, enumName))
            .Concat([GenerateGetDefaultCaseStatement()])
            .ToList();

        // The return type is nullable because a field whose schema admits null returns one. The
        // member being implemented is not nullable-annotated, so declaring it this way narrows
        // nothing for a caller and spares the generated body a suppression on every optional field.
        return MethodDeclaration(
                NullableType(
                    PredefinedType(
                        Token(SyntaxKind.ObjectKeyword))),
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

    private static MethodDeclarationSyntax GeneratePutMethod(IReadOnlyList<FieldShape> fields, string enumName, string localEnumVarName, CodeGenOptions options)
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

        var fieldCaseStatements = fields
            .Select(f => GeneratePutCaseStatement(f, enumName, options))
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

    private static SwitchExpressionArmSyntax GenerateGetCaseStatement(FieldShape field, string enumClassName)
    {
        // A decimal property must be converted back to the AvroDecimal the writer expects.
        var decimalSchema = field.ConvertedDecimalSchema;
        var valueExpression = decimalSchema != null
            ? GenerateGetDecimalCase(field.Name, field.PropertyName, AvroSchemaUtilities.GetDecimalScale(decimalSchema), field.IsNullable)
            : SyntaxUtilities.SafeIdentifierName(field.PropertyName);

        return SwitchExpressionArm(
            ConstantPattern(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName(enumClassName),
                    SyntaxUtilities.SafeIdentifierName(field.Name))),
            valueExpression);
    }

    private static ExpressionSyntax GenerateGetDecimalCase(string fieldName, string propertyName, int scale, bool isNullable)
    {
        // Only the non-null branch is converted, so a nullable decimal keeps its null as-is.
        var value = isNullable
            ? MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxUtilities.SafeIdentifierName(propertyName),
                IdentifierName(nameof(Nullable<int>.Value)))
            : (ExpressionSyntax)SyntaxUtilities.SafeIdentifierName(propertyName);

        var conversion = GenerateAvroDecimalConversion(value, fieldName, scale);

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

    /// <summary>
    /// Builds the expression that hands a decimal property to Avro. Avro refuses to write an
    /// <see cref="AvroDecimal"/> whose scale is not the one the schema declares, so a value with
    /// fewer decimal places is padded out to the schema's scale by adding a zero that carries it.
    /// A value with more decimal places than the schema can store is reported rather than rounded,
    /// so that digits are never dropped on the way to the wire.
    /// </summary>
    private static ExpressionSyntax GenerateAvroDecimalConversion(ExpressionSyntax value, string fieldName, int scale)
    {
        var scaleLiteral = LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(scale));

        var valueFitsTheScale = BinaryExpression(
            SyntaxKind.EqualsExpression,
            InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    MathType,
                    IdentifierName(nameof(Math.Round))))
            .WithArgumentList(
                ArgumentList(
                    SeparatedList<ArgumentSyntax>(
                        new SyntaxNodeOrToken[]
                        {
                                Argument(value),
                                Token(SyntaxKind.CommaToken),
                                Argument(scaleLiteral)
                        }))),
            value);

        var scalePadding = ObjectCreationExpression(
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
                                Argument(scaleLiteral)
                        })));

        var conversion = ObjectCreationExpression(
            AvroSchemaUtilities.AvroDecimalType)
            .WithArgumentList(
                ArgumentList(
                    SingletonSeparatedList(
                        Argument(
                            BinaryExpression(
                                SyntaxKind.AddExpression,
                                value,
                                scalePadding)))));

        return ConditionalExpression(
            valueFitsTheScale,
            conversion,
            GenerateDecimalPrecisionLossThrow(value, fieldName, scale));
    }

    private static ThrowExpressionSyntax GenerateDecimalPrecisionLossThrow(ExpressionSyntax value, string fieldName, int scale)
    {
        var message = BinaryExpression(
            SyntaxKind.AddExpression,
            BinaryExpression(
                SyntaxKind.AddExpression,
                LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    Literal($"Cannot write field '{fieldName}': the value ")),
                value),
            LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                Literal($" has more decimal places than the schema's scale of {scale.ToString(CultureInfo.InvariantCulture)}.")));

        return ThrowExpression(
            ObjectCreationExpression(
                AvroTypeExceptionType)
            .WithArgumentList(
                ArgumentList(
                    SingletonSeparatedList(
                        Argument(message)))));
    }

    private static SwitchExpressionArmSyntax GenerateGetDefaultCaseStatement()
    {
        return SwitchExpressionArm(
            DiscardPattern(),
            ThrowExpression(
                ObjectCreationExpression(
                    AvroRuntimeExceptionType)
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
                            AvroRuntimeExceptionType)
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

    private static SwitchSectionSyntax GeneratePutCaseStatement(FieldShape field, string enumClassName, CodeGenOptions options)
    {
        // A decimal property must be converted from the AvroDecimal the reader supplies.
        if (field.ConvertedDecimalSchema != null)
        {
            return GenerateDecimalPutCaseStatement(field, enumClassName, options);
        }

        var fieldType = field.Type;
        var assignmentTargetName = options.InitOnlyProperties ? field.BackingFieldName : field.PropertyName;

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

    private static SwitchSectionSyntax GenerateDecimalPutCaseStatement(FieldShape field, string enumClassName, CodeGenOptions options)
    {
        var assignmentTargetName = options.InitOnlyProperties ? field.BackingFieldName : field.PropertyName;

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
        if (field.IsNullable)
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
