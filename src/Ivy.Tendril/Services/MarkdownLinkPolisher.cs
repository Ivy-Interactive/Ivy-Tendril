using System.Text.RegularExpressions;

namespace Ivy.Tendril.Services;

public class MarkdownLinkPolisher
{
    private static readonly Regex MarkdownLinkRegex = new(
        @"\[([^\]]*)\]\(([^)]+)\)",
        RegexOptions.Compiled);

    private static readonly Regex FileLinkRegex = new(
        @"^file:///(.+?)(?:#(\d+))?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PlanRevisionLinkRegex = new(
        @"file:///.*?/Plans/(\d{5})-[^/]+/revisions/\d{3}\.md$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BarePlanNumberRegex = new(
        @"(?<!\[)(?<!\d)(\d{5})(?!\d)(?!\]\()(?![^[]*\])(?!\))",
        RegexOptions.Compiled);

    private static readonly Regex PlanContextRegex = new(
        @"\bPlans?\s+((?:\d{5})(?:\s*,\s*\d{5})*)",
        RegexOptions.Compiled);

    private static readonly Regex BacktickLinkTextRegex = new(
        @"\[`([^`\]]+)`\]\((file:///[^)]+)\)",
        RegexOptions.Compiled);

    public string PolishLinks(string markdownContent, IEnumerable<string> repoPaths, string planFolder)
    {
        if (string.IsNullOrEmpty(markdownContent))
            return markdownContent;

        var repoPathsList = repoPaths.ToList();
        var result = markdownContent;

        result = RemoveBackticksFromFileLinkText(result);
        result = PolishMarkdownLinks(result, repoPathsList, planFolder);
        result = ConvertBarePlanNumbers(result, planFolder);

        return result;
    }

    private string RemoveBackticksFromFileLinkText(string content)
    {
        return BacktickLinkTextRegex.Replace(content, match =>
        {
            var text = match.Groups[1].Value;
            var url = match.Groups[2].Value;
            return $"[{text}]({url})";
        });
    }

    private string PolishMarkdownLinks(string content, List<string> repoPaths, string planFolder)
    {
        return MarkdownLinkRegex.Replace(content, match =>
        {
            var linkText = match.Groups[1].Value;
            var url = match.Groups[2].Value;

            if (url.StartsWith("plan://", StringComparison.OrdinalIgnoreCase))
                return match.Value;

            var planRevisionMatch = PlanRevisionLinkRegex.Match(url);
            if (planRevisionMatch.Success)
            {
                var planId = planRevisionMatch.Groups[1].Value;
                return $"[{linkText}](plan://{planId})";
            }

            var fileLinkMatch = FileLinkRegex.Match(url);
            if (!fileLinkMatch.Success)
                return match.Value;

            var filePath = fileLinkMatch.Groups[1].Value;
            var hasAnchor = fileLinkMatch.Groups[2].Success;

            filePath = NormalizePath(filePath);

            if (!File.Exists(filePath))
            {
                var fileName = Path.GetFileName(filePath);
                var found = MarkdownHelper.FindFilesInRepos(repoPaths, fileName);
                if (found.Count == 1)
                    filePath = found[0].Replace('\\', '/');
                else
                    return hasAnchor
                        ? $"[{linkText}](file:///{filePath})"
                        : match.Value;
            }

            return $"[{linkText}](file:///{filePath})";
        });
    }

    private string ConvertBarePlanNumbers(string content, string planFolder)
    {
        var plansDirectory = Path.GetDirectoryName(planFolder);
        if (string.IsNullOrEmpty(plansDirectory) || !Directory.Exists(plansDirectory))
            return content;

        return PlanContextRegex.Replace(content, match =>
        {
            var prefix = match.Value.StartsWith("Plans", StringComparison.Ordinal) ? "Plans " : "Plan ";
            var numbersText = match.Groups[1].Value;
            var numbers = Regex.Split(numbersText, @"\s*,\s*");

            var converted = numbers.Select(num =>
            {
                var paddedId = num.PadLeft(5, '0');
                var planExists = Directory.GetDirectories(plansDirectory, $"{paddedId}-*").Length > 0;
                return planExists ? $"[{num}](plan://{paddedId})" : num;
            });

            return prefix + string.Join(", ", converted);
        });
    }

    internal static string NormalizePath(string path)
    {
        path = path.Replace('\\', '/');
        path = Regex.Replace(path, @"/{2,}", "/");

        if (path.Length >= 2 && path[1] == ':')
            path = path[0] + ":" + path.Substring(2);

        var parts = path.Split('/');
        var stack = new List<string>();
        foreach (var part in parts)
        {
            if (part == ".." && stack.Count > 0 && stack[^1] != "..")
                stack.RemoveAt(stack.Count - 1);
            else if (part != ".")
                stack.Add(part);
        }

        return string.Join("/", stack);
    }
}
