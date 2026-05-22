using System.Text.Json;

namespace Ivy.Tendril.Agents.Helpers;

internal static class ContentExtractor
{
    /// <summary>
    /// Extracts text from a JSON element that may be:
    /// - A plain string: "hello"
    /// - An array of content blocks: [{"type":"text","text":"hello"},...]
    /// - Null or missing
    /// </summary>
    internal static string? ExtractText(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString();

            case JsonValueKind.Array:
                string? result = null;
                foreach (var block in element.EnumerateArray())
                {
                    if (block.ValueKind == JsonValueKind.String)
                    {
                        result = Append(result, block.GetString());
                        continue;
                    }

                    if (block.ValueKind != JsonValueKind.Object) continue;

                    if (block.TryGetProperty("text", out var textProp) &&
                        textProp.ValueKind == JsonValueKind.String)
                    {
                        result = Append(result, textProp.GetString());
                    }
                }
                return result;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;

            default:
                return element.GetRawText();
        }
    }

    private static string? Append(string? existing, string? addition)
    {
        if (addition is null) return existing;
        if (existing is null) return addition;
        return existing + "\n" + addition;
    }
}
