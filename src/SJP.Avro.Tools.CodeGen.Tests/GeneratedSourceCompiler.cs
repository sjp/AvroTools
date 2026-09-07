using System;
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
    /// Compiles one or more generated source files into a single assembly and loads it.
    /// </summary>
    /// <param name="sources">Generated C# source files, which may refer to each other.</param>
    /// <returns>The loaded assembly.</returns>
    public static Assembly Compile(params string[] sources)
    {
        var syntaxTrees = Array.ConvertAll(
            sources,
            source => CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)));

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "GeneratedAssembly_" + Guid.NewGuid().ToString("N"),
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var assemblyStream = new MemoryStream();
        var emitResult = compilation.Emit(assemblyStream);

        Assert.That(emitResult.Success, Is.True, () => string.Join(Environment.NewLine, emitResult.Diagnostics.Select(d => d.ToString())));

        // Loading into the default context keeps the generated types visible to Avro's
        // ObjectCreator, which resolves specific types by name across loaded assemblies.
        return Assembly.Load(assemblyStream.ToArray());
    }

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
