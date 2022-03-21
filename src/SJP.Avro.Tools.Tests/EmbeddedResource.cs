using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SJP.Avro.Tools.Tests;

internal static class EmbeddedResource
{
    private static readonly ConcurrentDictionary<string, Lazy<string>> _cache = new();

    public static string GetByName(string fileName) => _cache.GetOrAdd(fileName, new Lazy<string>(() => GetStringResourceByName(fileName))).Value;

    private static string GetStringResourceByName(string resourceName)
    {
        if (string.IsNullOrWhiteSpace(resourceName))
            throw new ArgumentNullException(nameof(resourceName));

        var asm = Assembly.GetExecutingAssembly();
        var namePrefix = asm.GetName().Name + ".";
        var qualifiedName = namePrefix + resourceName;

        using var stream = asm.GetManifestResourceStream(qualifiedName)!;
        using var memStream = new MemoryStream();
        stream.CopyTo(memStream);
        return Encoding.UTF8.GetString(memStream.ToArray());
    }

    public static IEnumerable<string> GetEmbeddedResourceNames()
    {
        var asm = Assembly.GetExecutingAssembly();
        var resourceNames = asm.GetManifestResourceNames();

        var namePrefix = asm.GetName().Name + ".";

        return resourceNames
            .Select(n => n[namePrefix.Length..])
            .ToList();
    }
}
