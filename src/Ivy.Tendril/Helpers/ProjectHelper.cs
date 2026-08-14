using Ivy.Tendril.Services;

namespace Ivy.Tendril.Helpers;

public static class ProjectHelper
{
    /// <summary>
    ///     Builds a project-name-to-color-name mapping (e.g. for
    ///     <see cref="LabelsDisplayRenderer.BadgeColorMapping"/>) from the configured projects'
    ///     colors. Projects without a configured color are omitted.
    /// </summary>
    public static Dictionary<string, string> BuildColorMapping(IConfigService config)
    {
        return config.Projects
            .Select(p => new { p.Name, Color = config.GetProjectColor(p.Name) })
            .Where(x => x.Color.HasValue)
            .ToDictionary(x => x.Name, x => x.Color!.Value.ToString());
    }

    public static string[] ParseProjects(string? projectValue)
    {
        if (string.IsNullOrWhiteSpace(projectValue))
            return Array.Empty<string>();

        return projectValue
            .Split(',')
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrEmpty(p))
            .ToArray();
    }

    public static bool ContainsProject(string? projectValue, string projectToFind)
    {
        if (string.IsNullOrWhiteSpace(projectValue) || string.IsNullOrWhiteSpace(projectToFind))
            return false;

        var projects = ParseProjects(projectValue);
        return projects.Contains(projectToFind, StringComparer.OrdinalIgnoreCase);
    }

    public static string FormatProjectsForDisplay(string? projectValue)
    {
        var projects = ParseProjects(projectValue);
        return projects.Length > 0 ? string.Join(", ", projects) : "Auto";
    }
}
