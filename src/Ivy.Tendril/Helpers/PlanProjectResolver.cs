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
        {
            if (Directory.Exists(projectName))
            {
                var fullPath = Path.GetFullPath(projectName);
                var folderName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrEmpty(folderName))
                {
                    folderName = fullPath;
                }

                return new ProjectConfig
                {
                    Name = folderName,
                    Repos = [new RepoRef { Path = fullPath }],
                    Meta = new Dictionary<string, object> { ["adhoc"] = true }
                };
            }

            throw new ArgumentException($"Project '{projectName}' not found. Available: {namesList}");
        }

        if (project.Repos.Count == 0)
            throw new ArgumentException($"Project '{project.Name}' has no repos configured.");

        return project;
    }
}
