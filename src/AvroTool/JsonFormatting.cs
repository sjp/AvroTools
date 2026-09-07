using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AvroTool;

/// <summary>
/// Shared JSON pretty-printing for commands that offer a <c>--pretty</c> option.
/// </summary>
internal static class JsonFormatting
{
    /// <summary>
    /// Serializer options producing indented output. <see cref="JsonSerializerOptions"/> caches
    /// per-instance metadata, so a single shared instance is reused rather than one per call.
    /// </summary>
    /// <remarks>
    /// The relaxed encoder writes non-ASCII text and the characters <c>&lt;</c>, <c>&gt;</c>,
    /// <c>&amp;</c>, <c>'</c> and <c>+</c> literally instead of as <c>\uXXXX</c> escapes. Output
    /// goes to UTF-8 files and standard output rather than into HTML, so escaping those characters
    /// only makes documentation and default values unreadable, and makes the result differ from
    /// what other Avro tooling writes. Control characters are still escaped.
    /// </remarks>
    public static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Re-serializes the given JSON text with indentation.
    /// </summary>
    public static string Indent(string json)
    {
        var node = JsonNode.Parse(json);
        return node!.ToJsonString(IndentedOptions);
    }
}
