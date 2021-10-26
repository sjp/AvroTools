using System;
using System.Linq;

namespace SJP.Avro.Tools.Tests
{
    internal class EmbeddedResourceFileProvider : IFileProvider
    {
        public string GetFileContents(string basePath, string filePath)
        {
            var resolvedPath = CreatePath(basePath, filePath);

            return EmbeddedResource.GetByName(resolvedPath);
        }

        public string CreatePath(string basePath, string filePath)
        {
            var pieces = basePath.Split('.', StringSplitOptions.RemoveEmptyEntries);
            var fileNameRemoved = pieces[..^2]; // assumes file extension present!

            return fileNameRemoved.Concat(new[] { filePath }).Join(".");
        }

        public static IFileProvider Instance { get; } = new EmbeddedResourceFileProvider();
    }
}
