using System.Text.RegularExpressions;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Helpers;

public static partial class MarkdownHelper
{
    private static readonly Regex FileLinkRegex = _FileLinkRegex();

    private static readonly Regex PlanLinkRegex = _PlanLinkRegex();

    /// <summary>
    ///     Annotates broken file:/// links in markdown content with a warning indicator.
    ///     Valid links are left unchanged.
    /// </summary>
    public static string AnnotateBrokenFileLinks(string markdownContent)
    {
        if (string.IsNullOrEmpty(markdownContent))
            return markdownContent;

        return FileLinkRegex.Replace(markdownContent, match =>
        {
            var linkText = match.Groups[1].Value;
            var url = match.Groups[2].Value;
            var filePath = url["file:///".Length..];

            if (File.Exists(filePath))
                return match.Value;

            return $"[{linkText} \u26a0\ufe0f]({url})";
        });
    }

    public static string AnnotateBrokenPlanLinks(string markdownContent, string plansDirectory)
    {
        if (string.IsNullOrEmpty(markdownContent))
            return markdownContent;

        return PlanLinkRegex.Replace(markdownContent, match =>
        {
            var linkText = match.Groups[1].Value;
            var url = match.Groups[2].Value;
            var planId = match.Groups[3].Value;

            var paddedId = planId.PadLeft(5, '0');

            var planExists = Directory.Exists(plansDirectory) &&
                             Directory.GetDirectories(plansDirectory, $"{paddedId}-*").Length > 0;

            if (planExists)
                return match.Value;

            return $"[{linkText} \u26a0\ufe0f]({url})";
        });
    }

    /// <summary>
    ///     Annotates both broken file:/// and plan:// links in markdown content with warning indicators.
    ///     Combines AnnotateBrokenFileLinks and AnnotateBrokenPlanLinks into a single call.
    /// </summary>
    public static string AnnotateAllBrokenLinks(string markdownContent, string plansDirectory)
    {
        if (string.IsNullOrEmpty(markdownContent))
            return markdownContent;

        var annotated = AnnotateBrokenFileLinks(markdownContent);
        return AnnotateBrokenPlanLinks(annotated, plansDirectory);
    }

    /// <summary>
    ///     Prepares agent-authored markdown for display: repairs links with
    ///     <see cref="MarkdownLinkPolisher" /> (via <see cref="IConfigService.PolishMarkdown" />), then
    ///     annotates any still-broken file:/// and plan:// links with a ⚠️ indicator. This is the
    ///     safety net for content that never went through <see cref="RevisionWriter" /> — legacy
    ///     revisions already on disk, and non-revision markdown (recommendation descriptions,
    ///     summaries, verification reports). Idempotent, so double-polishing persisted content is a no-op.
    /// </summary>
    public static string PrepareForDisplay(string markdownContent, IConfigService config)
    {
        if (string.IsNullOrEmpty(markdownContent))
            return markdownContent;

        return AnnotateAllBrokenLinks(config.PolishMarkdown(markdownContent), config.PlanFolder);
    }

    [GeneratedRegex(@"\[([^\]]*)\]\((file:///[^)]+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex _FileLinkRegex();

    [GeneratedRegex(@"\[([^\]]*)\]\((plan://(\d{1,5}))\)", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex _PlanLinkRegex();
}
