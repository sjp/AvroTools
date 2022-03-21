using System;
using System.Linq;

namespace SJP.Avro.Tools.Tests;

internal class EmbeddedResourceFileProvider : IFileProvider
{
    public string GetFileContents(string basePath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
            throw new ArgumentNullException(nameof(basePath));
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentNullException(nameof(relativePath));

        var resolvedPath = CreatePath(basePath, relativePath);
        return EmbeddedResource.GetByName(resolvedPath);
    }

    public string CreatePath(string basePath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
            throw new ArgumentNullException(nameof(basePath));
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentNullException(nameof(relativePath));

        var pieces = basePath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var fileNameRemoved = pieces[..^2]; // assumes file extension present!

        return fileNameRemoved.Concat(new[] { relativePath }).Join(".");
    }

    public static IFileProvider Instance { get; } = new EmbeddedResourceFileProvider();
}