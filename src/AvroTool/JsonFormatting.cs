using System.Text.Json;
using System.Text.Json.Nodes;

namespace AvroTool;

/// <summary>
/// Shared JSON pretty-printing for commands that offer a <c>--pretty</c> option.
/// </summary>
internal static class JsonFormatting
{
    /// <summary>
    /// Re-serializes the given JSON text with indentation.
    /// </summary>
    public static string Indent(string json)
    {
        var node = JsonNode.Parse(json);
        return node!.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
