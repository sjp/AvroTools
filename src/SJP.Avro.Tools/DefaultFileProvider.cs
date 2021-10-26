using System.IO;

namespace SJP.Avro.Tools
{
    public class DefaultFileProvider : IFileProvider
    {
        public string CreatePath(string basePath, string filePath)
        {
            var dir = Path.GetDirectoryName(basePath) ?? basePath;
            return Path.Combine(dir, filePath);
        }

        public string GetFileContents(string basePath, string filePath)
        {
            var resolvedPath = CreatePath(basePath, filePath);

            return File.ReadAllText(resolvedPath);
        }
    }
}
