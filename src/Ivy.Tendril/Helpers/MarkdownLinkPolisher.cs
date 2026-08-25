using System.Text.RegularExpressions;

namespace Ivy.Tendril.Helpers;

public class MarkdownLinkPolisher
{
    private static readonly Regex MarkdownLinkRegex = new(
        @"\[([^\]]*)\]\(([^)]+)\)",
        RegexOptions.Compiled);

    private static readonly Regex FileLinkRegex = new(
        @"^file:///(.+?)(?:(?:#L?|:)(\d+(?:-\d+)?))?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PlanRevisionLinkRegex = new(
        @"file:///.*?/Plans/(\d{5})-[^/]+/revisions/\d{3}\.md$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PlanContextRegex = new(
        @"\bPlans?\s+((?:\d{5})(?:\s*,\s*\d{5})*)",
        RegexOptions.Compiled);

    private static readonly Regex BacktickLinkTextRegex = new(
        @"\[`([^`\]]+)`\]\((file:///[^)]+)\)",
        RegexOptions.Compiled);

    // Fenced/inline code patterns, shared by both protected-span regexes below — the sample must
    // survive verbatim wherever it appears.
    private const string ProtectedCodeSpanPattern =
        @"```[\s\S]*?```" +          // fenced code (```)
        @"|~~~[\s\S]*?~~~" +         // fenced code (~~~)
        @"|``[^\n]*?``" +            // double-backtick inline code
        @"|`[^`\n]*`";               // single-backtick inline code

    // Spans the nested-link-collapse pass must not rewrite: code only. It deliberately does NOT
    // reuse ProtectedSpanRegex's link alternative below — that pass's own target *is* a link, and the
    // non-recursive "whole link" alternative would greedily consume the inner link and misreport the
    // nested match's start as already protected.
    private static readonly Regex ProtectedCodeSpanRegex = new(
        ProtectedCodeSpanPattern,
        RegexOptions.Compiled);

    // Spans the bare-number pass must not rewrite: code (above) plus existing markdown links/images
    // (the link text is where the corruption happens — protecting only the URL is not enough).
    private static readonly Regex ProtectedSpanRegex = new(
        ProtectedCodeSpanPattern +
        @"|!?\[[^\]]*\]\([^)]*\)",   // markdown link or image: link text AND url
        RegexOptions.Compiled);

    private static readonly Regex NestedPlanLinkRegex = new(
        @"\[([^\[\]]*)\[([^\[\]]*)\]\(plan://\d{1,5}\)([^\[\]]*)\]\((plan://\d{1,5})\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string PolishLinks(string markdownContent, string plansDirectory)
    {
        if (string.IsNullOrEmpty(markdownContent))
            return markdownContent;

        var result = markdownContent;

        result = RemoveBackticksFromFileLinkText(result);
        result = PolishMarkdownLinks(result);
        result = CollapseNestedPlanLinks(result);
        result = ConvertBarePlanNumbers(result, plansDirectory);

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

    private string PolishMarkdownLinks(string content)
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
            var anchor = fileLinkMatch.Groups[2].Success ? fileLinkMatch.Groups[2].Value : null;

            filePath = NormalizePath(filePath);

            // Normalize the path and strip the line anchor, but never repo-scan to redirect a
            // missing path onto a same-basename file: that silently points at the wrong file and
            // is expensive on the render thread. Links to files that don't exist are left as
            // authored and surfaced by AnnotateBrokenFileLinks (a ⚠️) at display time.
            var simplifiedText = SimplifyLinkText(linkText, filePath, anchor);
            return $"[{simplifiedText}](file:///{filePath})";
        });
    }

    private string SimplifyLinkText(string linkText, string filePath, string? anchor)
    {
        var fileName = Path.GetFileName(filePath);

        // Pattern: link text is verbose file:///path or file:///path:line
        if (linkText.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
        {
            var textMatch = FileLinkRegex.Match(linkText);
            if (textMatch.Success)
            {
                var textAnchor = textMatch.Groups[2].Success ? textMatch.Groups[2].Value : null;
                return textAnchor != null
                    ? $"{Path.GetFileName(textMatch.Groups[1].Value)}:{textAnchor}"
                    : Path.GetFileName(textMatch.Groups[1].Value);
            }
        }

        // Pattern: link text contains directory separators (full path without file:/// prefix)
        if (linkText.Contains(Path.DirectorySeparatorChar) || linkText.Contains(Path.AltDirectorySeparatorChar))
        {
            return anchor != null ? $"{fileName}:{anchor}" : fileName;
        }

        // Already simplified or no simplification needed
        return linkText;
    }

    private string ConvertBarePlanNumbers(string content, string plansDirectory)
    {
        if (string.IsNullOrEmpty(plansDirectory) || !Directory.Exists(plansDirectory))
            return content;

        var protectedSpans = GetProtectedSpans(content, ProtectedSpanRegex);

        return PlanContextRegex.Replace(content, match =>
        {
            if (IsWithinProtectedSpan(match.Index, protectedSpans))
                return match.Value;

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

    // Rewrites a plan link whose text was corrupted into containing a second nested plan link
    // (e.g. `[Plan [00050](plan://00050)](plan://00050)`) back to the authored form
    // (`[Plan 00050](plan://00050)`), keeping the outer URL since that's the one the author wrote.
    // Loops until the string stops changing so doubly nested content also settles.
    private string CollapseNestedPlanLinks(string content)
    {
        string previous;
        do
        {
            previous = content;
            var protectedSpans = GetProtectedSpans(content, ProtectedCodeSpanRegex);

            content = NestedPlanLinkRegex.Replace(content, match =>
            {
                if (IsWithinProtectedSpan(match.Index, protectedSpans))
                    return match.Value;

                var text = match.Groups[1].Value + match.Groups[2].Value + match.Groups[3].Value;
                var url = match.Groups[4].Value;
                return $"[{text}]({url})";
            });
        } while (content != previous);

        return content;
    }

    private static (int Start, int End)[] GetProtectedSpans(string content, Regex protectedSpanRegex)
    {
        return protectedSpanRegex.Matches(content)
            .Select(m => (Start: m.Index, End: m.Index + m.Length))
            .ToArray();
    }

    private static bool IsWithinProtectedSpan(int index, (int Start, int End)[] protectedSpans)
    {
        foreach (var span in protectedSpans)
        {
            if (index >= span.Start && index < span.End)
                return true;
        }

        return false;
    }

    internal static string NormalizePath(string path)
    {
        path = path.Replace('\\', '/');
        path = Regex.Replace(path, "/{2,}", "/");

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
