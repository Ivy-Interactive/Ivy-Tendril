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
                var gitRoot = GitHelper.ResolveGitRoot(fullPath);
                var targetRepoPath = gitRoot ?? fullPath;

                if (gitRoot != null)
                {
                    var normalizedGitRoot = NormalizeLocalPath(gitRoot);
                    var matchingProject = available.FirstOrDefault(p =>
                        p.Repos.Any(r => !string.IsNullOrWhiteSpace(r.Path) &&
                                         string.Equals(NormalizeLocalPath(r.Path), normalizedGitRoot, StringComparison.OrdinalIgnoreCase)));

                    if (matchingProject != null)
                    {
                        return matchingProject;
                    }
                }

                var folderName = Path.GetFileName(targetRepoPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrEmpty(folderName))
                {
                    folderName = targetRepoPath;
                }

                return new ProjectConfig
                {
                    Name = folderName,
                    Repos = [new RepoRef { Path = targetRepoPath }],
                    Meta = new Dictionary<string, object>
                    {
                        ["adhoc"] = true,
                        ["targetPath"] = fullPath
                    }
                };
            }

            throw new ArgumentException($"Project '{projectName}' not found. Available: {namesList}");
        }

        if (project.Repos.Count == 0)
            throw new ArgumentException($"Project '{project.Name}' has no repos configured.");

        return project;
    }

    private static string NormalizeLocalPath(string path)
    {
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(path);
            return Path.GetFullPath(expanded).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd('/', '\\');
        }
    }
}
