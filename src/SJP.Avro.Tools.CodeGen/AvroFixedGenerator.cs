using System;
using System.Collections.Generic;
using Avro;
using Avro.Specific;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace SJP.Avro.Tools.CodeGen;

/// <summary>
/// Generates C# class files for Avro fixed types.
/// </summary>
public class AvroFixedGenerator : ICodeGenerator<FixedSchema>
{
    /// <summary>
    /// Creates a C# implementation of an Avro fixed type.
    /// </summary>
    /// <param name="schema">A definition of a fixed type in Avro schema.</param>
    /// <param name="baseNamespace">The base namespace to use (when one is absent).</param>
    /// <param name="options">Ignored. Fixed types have no per-field properties for output style options to affect.</param>
    /// <returns>A string representing a C# file containing a class definition.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="schema"/> or <paramref name="baseNamespace"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="baseNamespace"/> is empty or whitespace and <paramref name="schema"/> does not declare a namespace.</exception>
    /// <exception cref="NotSupportedException"><paramref name="schema"/> is named after a member its generated class is obliged to declare.</exception>
    public string Generate(FixedSchema schema, string baseNamespace, CodeGenOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(baseNamespace);

        var typeName = schema.Name;

        if (string.Equals(typeName, AvroSchemaUtilities.SchemaMemberName, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"The fixed type '{schema.Fullname}' cannot be generated. A fixed type is generated as a class deriving "
                + $"from {typeof(SpecificFixed).FullName}, which obliges it to declare a member named "
                + $"{AvroSchemaUtilities.SchemaMemberName}, and a C# type may not declare a member of its own name. "
                + "Rename the type in the schema.");
        }

        var ns = SyntaxUtilities.ResolveNamespace(schema.Namespace, baseNamespace, schema.Fullname);

        var namespaceDeclaration = NamespaceDeclaration(SyntaxUtilities.SafeNamespaceName(ns));

        // The schema field and the size property are the generator's own, so where the fixed type
        // is named after one of them it is the member that gives way, not the type.
        var reservedNames = new HashSet<string>(StringComparer.Ordinal) { typeName };
        var schemaFieldName = ReservedNames.MakeAvailable(AvroSchemaUtilities.SchemaFieldName, reservedNames);
        var fixedSizeName = ReservedNames.MakeAvailable(FixedSizePropertyName, reservedNames);

        var schemaField = AvroSchemaUtilities.CreateSchemaDefinition(schema.ToString(), schemaFieldName);
        var schemaProperty = AvroSchemaUtilities.CreateSchemaProperty(schemaFieldName)
            .WithModifiers(
                TokenList(
                    Token(SyntaxKind.PublicKeyword),
                    Token(SyntaxKind.OverrideKeyword)));
        var fixedSizeProp = CreateFixedSizeProperty(schema.Size, fixedSizeName);
        var ctor = CreateConstructor(typeName, fixedSizeName);

        var members = new MemberDeclarationSyntax[]
        {
                schemaField,
                schemaProperty,
                fixedSizeProp,
                ctor
        };

        var generatedClass = ClassDeclaration(SyntaxUtilities.SafeIdentifier(typeName))
            .AddModifiers(Token(SyntaxKind.PublicKeyword))
            .AddBaseListTypes(SimpleBaseType(SyntaxUtilities.GlobalName(typeof(SpecificFixed))))
            .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
            .WithMembers(List(members))
            .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken));

        generatedClass = generatedClass
            .WithLeadingTrivia(SyntaxUtilities.BuildCommentTrivia(schema.Documentation));

        return SyntaxUtilities.GenerateDocument(namespaceDeclaration, generatedClass);
    }

    /// <summary>
    /// The name of the property that states how many bytes the fixed type holds, before the type's
    /// own name lays claim to it.
    /// </summary>
    private const string FixedSizePropertyName = "FixedSize";

    private static ConstructorDeclarationSyntax CreateConstructor(string className, string fixedSizeName)
    {
        return ConstructorDeclaration(
            SyntaxUtilities.SafeIdentifier(className))
            .WithModifiers(
                TokenList(
                    Token(SyntaxKind.PublicKeyword)))
            .WithInitializer(
                ConstructorInitializer(
                    SyntaxKind.BaseConstructorInitializer,
                    ArgumentList(
                        SingletonSeparatedList(
                            Argument(
                                IdentifierName(fixedSizeName))))))
            .WithBody(Block());
    }

    private static PropertyDeclarationSyntax CreateFixedSizeProperty(int size, string fixedSizeName)
    {
        return PropertyDeclaration(
            PredefinedType(Token(SyntaxKind.UIntKeyword)),
                Identifier(fixedSizeName))
            .WithModifiers(
                TokenList(
                    Token(SyntaxKind.PublicKeyword),
                    Token(SyntaxKind.StaticKeyword)))
            .WithAccessorList(
                AccessorList(
                    SingletonList(
                        AccessorDeclaration(
                            SyntaxKind.GetAccessorDeclaration)
                        .WithSemicolonToken(
                            Token(SyntaxKind.SemicolonToken)))))
            .WithInitializer(
                EqualsValueClause(LiteralExpression(
                        SyntaxKind.NumericLiteralExpression,
                        Literal(size))))
            .WithSemicolonToken(
                Token(SyntaxKind.SemicolonToken));
    }
}
