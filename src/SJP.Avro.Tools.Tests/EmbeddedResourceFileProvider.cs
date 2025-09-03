using System;
using System.Linq;

namespace SJP.Avro.Tools.Tests;

internal class EmbeddedResourceFileProvider : IFileProvider
{
    public string GetFileContents(string basePath, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var resolvedPath = CreatePath(basePath, relativePath);
        return EmbeddedResource.GetByName(resolvedPath);
    }

    public string CreatePath(string basePath, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var pieces = basePath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var fileNameRemoved = pieces[..^2]; // assumes file extension present!

        return fileNameRemoved.Concat([relativePath]).Join(".");
    }

    public static IFileProvider Instance { get; } = new EmbeddedResourceFileProvider();
}