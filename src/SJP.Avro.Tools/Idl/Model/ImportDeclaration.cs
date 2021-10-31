using System;
using System.Text.RegularExpressions;

namespace SJP.Avro.Tools.Idl.Model
{
    /// <summary>
    /// Defines a declaration of an import of another Avro document.
    /// </summary>
    public record ImportDeclaration : NamedSchemaDeclaration
    {
        /// <summary>
        /// Constructs a declaration representing the importing of another Avro document.
        /// </summary>
        /// <param name="type">The type of document being imported.</param>
        /// <param name="path">The (relative) path of the document to import, relative to the parent document.</param>
        /// <param name="position">The position of the import within the source document.</param>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> is <c>null</c>, empty or whitespace.</exception>
        public ImportDeclaration(ImportType type, string path, int position)
            : base(null, Array.Empty<Property>(), position)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));
            if (type == ImportType.Unknown || !Enum.IsDefined<ImportType>(type))
                throw new ArgumentOutOfRangeException(nameof(type), $"Invalid import type provided. Received '{ (type == ImportType.Unknown ? "Unknown" : ((int)type).ToString()) }'");

            Type = type;
            Path = Unescape(path);
        }

        /// <summary>
        /// The type of document being imported.
        /// </summary>
        public ImportType Type { get; }

        /// <summary>
        /// The (relative) path of the document to import, relative to the parent document.
        /// </summary>
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
