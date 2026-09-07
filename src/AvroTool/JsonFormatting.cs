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
    public static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true
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
