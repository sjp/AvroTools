using System;
using System.Text.RegularExpressions;

namespace SJP.Avro.Tools.Idl.Model
{
    public record ImportDeclaration : NamedSchemaDeclaration
    {
        public ImportDeclaration(ImportType type, string path, int position)
            : base(null, Array.Empty<Property>(), position)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));

            Type = type;
            Path = Unescape(path);
        }

        public ImportType Type { get; }

        public string Path { get; }

        private static string Unescape(string value)
        {
            if (!value.StartsWith("\"") && !value.EndsWith("\""))
                return value;

            var trimmed = value[1..^1];
            return Regex.Unescape(trimmed);
        }
    }
}
