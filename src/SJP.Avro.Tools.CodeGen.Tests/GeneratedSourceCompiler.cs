using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SJP.Avro.Tools.CodeGen.Tests;

/// <summary>
/// Compiles generated source with Roslyn and loads the result, so that tests can assert on
/// behaviour rather than only on generated text.
/// </summary>
internal static class GeneratedSourceCompiler
{
    /// <summary>
    /// Compiles one or more generated source files into a single assembly and loads it, as a
    /// project that has not enabled nullable reference types would.
    /// </summary>
    /// <param name="sources">Generated C# source files, which may refer to each other.</param>
    /// <returns>The loaded assembly.</returns>
    public static Assembly Compile(params string[] sources) =>
        Compile(NullableContextOptions.Disable, sources);

    /// <summary>
    /// Compiles one or more source files into a single assembly and loads it.
    /// </summary>
    /// <param name="nullableContextOptions">The project-level nullable context to compile under.</param>
    /// <param name="sources">C# source files, which may refer to each other.</param>
    /// <returns>The loaded assembly.</returns>
    public static Assembly Compile(NullableContextOptions nullableContextOptions, params string[] sources) =>
        Compile(nullableContextOptions, DocumentationMode.Parse, sources);

    /// <summary>
    /// Compiles one or more generated source files with their documentation comments diagnosed
    /// rather than merely parsed, so that a doc comment which is not well-formed XML is reported
    /// instead of silently accepted.
    /// </summary>
    /// <param name="sources">Generated C# source files, which may refer to each other.</param>
    /// <returns>The loaded assembly.</returns>
    public static Assembly CompileWithDocumentationDiagnostics(params string[] sources) =>
        Compile(NullableContextOptions.Disable, DocumentationMode.Diagnose, sources);

    private static Assembly Compile(NullableContextOptions nullableContextOptions, DocumentationMode documentationMode, params string[] sources)
    {
        var syntaxTrees = Array.ConvertAll(
            sources,
            source => CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest, documentationMode)));

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToList();

        // Warnings are errors here so that generated code is held to what a consuming project that
        // builds with TreatWarningsAsErrors demands of it: no member hiding an inherited one, and
        // no nullability complaint.
        var compilation = CSharpCompilation.Create(
            "GeneratedAssembly_" + Guid.NewGuid().ToString("N"),
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: nullableContextOptions)
                .WithGeneralDiagnosticOption(ReportDiagnostic.Error)
                .WithSpecificDiagnosticOptions(TolerableDiagnosticOptions));

        using var assemblyStream = new MemoryStream();
        var emitResult = compilation.Emit(assemblyStream);

        Assert.That(emitResult.Success, Is.True, () => string.Join(Environment.NewLine, emitResult.Diagnostics.Select(static d => d.ToString())));

        // Loading into the default context keeps the generated types visible to Avro's
        // ObjectCreator, which resolves specific types by name across loaded assemblies.
        return Assembly.Load(assemblyStream.ToArray());
    }

    /// <summary>
    /// The warnings a generated file is allowed to raise. An Avro name is carried over verbatim, so
    /// a type may well end up named in all-lowercase ASCII (CS8981); that is a deliberate part of
    /// keeping the schema's own names, not a defect in the generated code. A schema documents only
    /// what its author chose to document, so an undocumented member (CS1591) or one whose summary
    /// says nothing about its parameters (CS1573) is likewise expected; malformed XML in a doc
    /// comment is not, and stays an error.
    /// </summary>
    private static readonly ImmutableDictionary<string, ReportDiagnostic> TolerableDiagnosticOptions =
        ImmutableDictionary<string, ReportDiagnostic>.Empty
            .Add("CS8981", ReportDiagnostic.Warn)
            .Add("CS1591", ReportDiagnostic.Warn)
            .Add("CS1573", ReportDiagnostic.Warn);

    /// <summary>
    /// Compiles generated source and returns one of the types it declares.
    /// </summary>
    /// <param name="source">A generated C# source file.</param>
    /// <param name="fullTypeName">The namespace-qualified name of the type to return.</param>
    /// <returns>The compiled type.</returns>
    public static Type CompileAndGetType(string source, string fullTypeName)
    {
        return Compile(source).GetType(fullTypeName)!;
    }
}
