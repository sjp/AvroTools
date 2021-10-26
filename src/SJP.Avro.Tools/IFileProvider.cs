namespace SJP.Avro.Tools
{
    public interface IFileProvider
    {
        string GetFileContents(string basePath, string filePath);

        string CreatePath(string basePath, string filePath);
    }
}