using Ivy.Tendril.Services;

namespace Ivy.Tendril.Helpers;

public static class PlanProjectResolver
{
    public static ProjectConfig ResolveProject(string? projectName, List<ProjectConfig> available)
    {
        var names = available.Select(p => p.Name).ToList();
        var namesList = string.Join(", ", names);

        if (string.IsNullOrWhiteSpace(projectName) ||
            projectName.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Project is required. Available: {namesList}");
        }

        var project = available.FirstOrDefault(p =>
            p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));

        if (project == null)
            throw new ArgumentException($"Project '{projectName}' not found. Available: {namesList}");

        if (project.Repos.Count == 0)
            throw new ArgumentException($"Project '{project.Name}' has no repos configured.");

        return project;
    }
}
