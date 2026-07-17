using System.Text;
using System.Text.RegularExpressions;

namespace Ivy.Tendril.Services.Slack;

public static partial class SlackTextFormatter
{
    [GeneratedRegex("<@([A-Z0-9]+)(?:\\|[^>]*)?>")]
    private static partial Regex UserMentionRegex();

    [GeneratedRegex("<(https?://[^|>]+)\\|([^>]+)>")]
    private static partial Regex LabeledLinkRegex();

    [GeneratedRegex("<(https?://[^>]+)>")]
    private static partial Regex PlainLinkRegex();

    [GeneratedRegex("<#[A-Z0-9]+\\|([^>]*)>")]
    private static partial Regex ChannelMentionRegex();

    public static string StripMention(string text, string botUserId)
    {
        var stripped = Regex.Replace(text, $"<@{Regex.Escape(botUserId)}>[ \\t]*", "");
        return stripped.Trim();
    }

    public static IReadOnlyList<string> ExtractUserMentions(string text) =>
        UserMentionRegex().Matches(text).Select(m => m.Groups[1].Value).Distinct().ToList();

    public static string Normalize(string text, Func<string, string> resolveUserName)
    {
        var result = UserMentionRegex().Replace(text, m => "@" + resolveUserName(m.Groups[1].Value));
        result = LabeledLinkRegex().Replace(result, m => $"{m.Groups[2].Value} ({m.Groups[1].Value})");
        result = PlainLinkRegex().Replace(result, m => m.Groups[1].Value);
        result = ChannelMentionRegex().Replace(result, m => "#" + m.Groups[1].Value);
        result = result.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");
        return result;
    }

    public static string BuildPlanDescription(
        string instruction,
        string requestedBy,
        string channelName,
        IReadOnlyList<(string Author, string Text)> context)
    {
        var sb = new StringBuilder();
        sb.AppendLine(instruction.Trim());
        sb.AppendLine();
        sb.AppendLine($"Requested by @{requestedBy} via Slack in #{channelName}.");

        if (context.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Slack conversation context (oldest first):");
            sb.AppendLine();
            foreach (var (author, text) in context)
            {
                var flattened = NormalizeWhitespace(text);
                if (flattened.Length > 0)
                    sb.AppendLine($"- @{author}: {flattened}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string NormalizeWhitespace(string text) =>
        string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
