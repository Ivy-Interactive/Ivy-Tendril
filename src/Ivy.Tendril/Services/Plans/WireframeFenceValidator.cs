using System.Globalization;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services.Wireframes;
using Ivy.Tendril.Wireframe.Hosting;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Ivy.Tendril.Services.Plans;

/// <summary>A <c>wireframe</c> fence's fields, once validated.</summary>
public sealed record WireframeFenceSpec(string Name, int? Height = null, string? Viewport = null);

/// <summary>
///     Validates the <c>wireframe</c> fences in plan revision markdown against the rules in
///     <c>Prompts/Plans.md</c> (<c>## Wireframes</c>). The field rules mirror the renderer's
///     <c>wireframeSource.ts</c>; keep the two in step.
///     <para>
///         Every problem is an error, so <c>write-revision</c> writes nothing until the agent fixes
///         them all. Four things are checked: that each block is well formed, that the wireframe it names exists in the plan folder, that every block sits in
///         a <c>## Wireframe</c> section which is the plan's first section, and that a revision embeds at
///         most <see cref="MaxPerRevision" /> of them. The cap is deliberate and not negotiable per plan: a
///         wireframe is for a UX decision, and a plan that wants more than two is usually several plans
///         or describing things prose would say better.
///     </para>
/// </summary>
public static class WireframeFenceValidator
{
    public const string InfoWord = "wireframe";
    public const string SectionHeading = "Wireframe";
    public const int MaxPerRevision = 2;
    public const int MaxHeight = 4000;

    private static bool IsWireframeHeading(string text) =>
        text.Equals(SectionHeading, StringComparison.OrdinalIgnoreCase) ||
        text.Equals(SectionHeading + "s", StringComparison.OrdinalIgnoreCase);

    private static readonly string[] Keys = ["name", "height", "viewport"];
    private static readonly string[] Viewports = ["Desktop", "Tablet", "Mobile"];

    /// <param name="markdown">The revision.</param>
    /// <param name="planFolder">
    ///     The plan the revision belongs to, so a block naming a wireframe that does not exist is
    ///     caught. Null skips that check.
    /// </param>
    public static IReadOnlyList<QuestionIssue> Validate(string markdown, string? planFolder)
    {
        var fences = QuestionBlockParser.FindFences(markdown, InfoWord);
        var issues = new List<QuestionIssue>();
        if (fences.Count == 0) return issues;

        // Wireframes are the first thing a reviewer sees: a `## Wireframe` section directly under the
        // title, before `## Problem`, holding every wireframe block the plan has.
        var sections = QuestionBlockParser.FindHeadings(markdown).Where(h => h.Level <= 2).ToList();
        var firstSection = sections.FirstOrDefault(h => h.Level == 2);
        var wireframeSection = sections.FirstOrDefault(h => h.Level == 2 && IsWireframeHeading(h.Text));

        if (wireframeSection.Text is not null && firstSection.Line != wireframeSection.Line)
        {
            issues.Add(Error(wireframeSection.Line,
                $"the `## {SectionHeading}` section must be the first section, directly under the title and before `## {firstSection.Text}`"));
        }

        for (var i = 0; i < fences.Count; i++)
        {
            var (line, body) = fences[i];

            var enclosing = sections.LastOrDefault(h => h.Line < line);
            if (enclosing.Text is null || enclosing.Level != 2 || !IsWireframeHeading(enclosing.Text))
            {
                issues.Add(Error(line,
                    $"put wireframe blocks in a `## {SectionHeading}` section directly under the title, not " +
                    (enclosing.Text is null ? "before it" : $"under `{new string('#', enclosing.Level)} {enclosing.Text}`")));
            }

            if (i == MaxPerRevision)
            {
                issues.Add(Error(line,
                    $"{fences.Count} wireframe blocks; a revision may embed at most {MaxPerRevision}. " +
                    "Keep the ones the design decision depends on and describe the rest in prose"));
            }

            var spec = Parse(body, out var error);
            if (spec is null)
            {
                issues.Add(Error(line, error!));
                continue;
            }

            if (planFolder is null) continue;

            var project = Path.Combine(planFolder, PlanWireframes.FolderName, spec.Name);
            if (!File.Exists(Path.Combine(project, "src", "main.tsx")))
            {
                issues.Add(Error(line,
                    $"this plan has no wireframe named '{spec.Name}'. Create it with: tendril wireframe setup \"{project}\""));
            }
        }

        return issues;
    }

    /// <summary>Reads one fence body, or returns null with the reason in <paramref name="error" />.</summary>
    public static WireframeFenceSpec? Parse(string body, out string? error)
    {
        error = null;
        var text = body.Trim();

        if (text.Length == 0)
            return Fail(out error, "the block names no wireframe; add a line such as 'name: checkout-payment'");

        if (WireframeHost.IsValidName(text))
            return new WireframeFenceSpec(text);

        YamlNode? root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            root = stream.Documents.Count > 0 ? stream.Documents[0].RootNode : null;
        }
        catch (YamlException)
        {
            return Fail(out error, "the block is not valid YAML");
        }

        if (root is not YamlMappingNode mapping)
            return Fail(out error, "write the block as 'name: <wireframe>', optionally with height and viewport");

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (keyNode, valueNode) in mapping.Children)
        {
            var key = (keyNode as YamlScalarNode)?.Value ?? "";
            if (!Keys.Contains(key))
                return Fail(out error, $"unknown key '{key}'; use name, height or viewport");
            if (valueNode is not YamlScalarNode scalar)
                return Fail(out error, $"{key} must be a single value");
            values[key] = scalar.Value ?? "";
        }

        if (!values.TryGetValue("name", out var name) || !WireframeHost.IsValidName(name))
            return Fail(out error, "name must be a lowercase slug such as checkout-payment");

        int? height = null;
        if (values.TryGetValue("height", out var rawHeight))
        {
            if (!int.TryParse(rawHeight, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                || parsed < 1 || parsed > MaxHeight)
                return Fail(out error, $"height must be a whole number of pixels between 1 and {MaxHeight}");
            height = parsed;
        }

        string? viewport = null;
        if (values.TryGetValue("viewport", out var rawViewport))
        {
            if (!Viewports.Contains(rawViewport, StringComparer.Ordinal))
                return Fail(out error, "viewport must be Desktop, Tablet or Mobile");
            viewport = rawViewport;
        }

        return new WireframeFenceSpec(name, height, viewport);
    }

    private static WireframeFenceSpec? Fail(out string? error, string message)
    {
        error = message;
        return null;
    }

    private static QuestionIssue Error(int line, string message) =>
        new(QuestionIssueSeverity.Error, line, "wireframe: " + message);
}
