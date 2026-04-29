using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ivy.Tendril.Apps.Plans;

internal static class PlanAdjustmentHelper
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public sealed record Adjustment(
        [property: JsonPropertyName("paragraphIndex")] int ParagraphIndex,
        [property: JsonPropertyName("text")] string Text);

    private sealed record Payload(
        [property: JsonPropertyName("adjustments")] List<Adjustment>? Adjustments);

    public static IReadOnlyList<Adjustment> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<Adjustment>();
        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(json, JsonOpts);
            return payload?.Adjustments ?? (IReadOnlyList<Adjustment>)Array.Empty<Adjustment>();
        }
        catch (JsonException)
        {
            return Array.Empty<Adjustment>();
        }
    }

    public static string ApplyAdjustments(string content, IReadOnlyList<Adjustment> adjustments)
    {
        var lines = content.Split('\n').ToList();
        var blocks = SplitMarkdownBlocks(lines);

        var ordered = adjustments
            .Where(a => a.ParagraphIndex >= 0
                        && a.ParagraphIndex < blocks.Count
                        && !string.IsNullOrWhiteSpace(a.Text))
            .OrderByDescending(a => a.ParagraphIndex);

        foreach (var adj in ordered)
        {
            var (_, endExclusive) = blocks[adj.ParagraphIndex];
            var insertion = new List<string> { "" };
            insertion.AddRange(
                adj.Text.Replace("\r\n", "\n").Split('\n').Select(l => $">> {l}"));
            lines.InsertRange(endExclusive, insertion);
        }

        return string.Join("\n", lines);
    }

    // Mirrors splitMarkdownIntoBlocks in Ivy.Widgets.PlanAdjuster's frontend so
    // paragraphIndex from the widget maps to the same block here.
    private static List<(int Start, int EndExclusive)> SplitMarkdownBlocks(List<string> lines)
    {
        var blocks = new List<(int, int)>();
        int? blockStart = null;
        var inFence = false;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("```"))
                inFence = !inFence;

            var endsBlock = !inFence && string.IsNullOrWhiteSpace(line);
            if (endsBlock)
            {
                if (blockStart is int s)
                    blocks.Add((s, i));
                blockStart = null;
            }
            else
            {
                blockStart ??= i;
            }
        }

        if (blockStart is int finalStart)
            blocks.Add((finalStart, lines.Count));

        return blocks;
    }
}
