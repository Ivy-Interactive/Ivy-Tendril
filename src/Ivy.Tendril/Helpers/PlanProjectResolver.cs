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
                    var normalizedFullPath = NormalizeLocalPath(fullPath);

                    var candidateProjects = available.Where(p =>
                        p.Repos.Any(r => RepoBelongsToGitRoot(r, normalizedGitRoot))).ToList();

                    if (candidateProjects.Count == 1)
                    {
                        var singleCandidate = candidateProjects[0];
                        var subdirs = GetProjectSubdirectories(singleCandidate, normalizedGitRoot);
                        if (subdirs.Count > 0)
                        {
                            if (subdirs.Any(s => IsSameOrSubdirectory(normalizedFullPath, s)))
                            {
                                return singleCandidate;
                            }
                            // Subdirectories conflict with fullPath: fall through to ad-hoc project creation
                        }
                        else
                        {
                            return singleCandidate;
                        }
                    }
                    else if (candidateProjects.Count > 1)
                    {
                        var matchingWithLength = candidateProjects
                            .Select(p =>
                            {
                                var subdirs = GetProjectSubdirectories(p, normalizedGitRoot);
                                var matching = subdirs.Where(s => IsSameOrSubdirectory(normalizedFullPath, s)).ToList();
                                return new
                                {
                                    Project = p,
                                    Subdirs = subdirs,
                                    MaxMatchLength = matching.Count > 0 ? matching.Max(s => s.Length) : -1
                                };
                            })
                            .ToList();

                        var bestMatch = matchingWithLength
                            .Where(x => x.MaxMatchLength >= 0)
                            .OrderByDescending(x => x.MaxMatchLength)
                            .FirstOrDefault();

                        if (bestMatch != null)
                        {
                            return bestMatch.Project;
                        }

                        var rootProject = matchingWithLength
                            .FirstOrDefault(x => x.Subdirs.Count == 0);

                        if (rootProject != null)
                        {
                            return rootProject.Project;
                        }

                        return candidateProjects[0];
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

    private static bool RepoBelongsToGitRoot(RepoRef repo, string normalizedGitRoot)
    {
        if (string.IsNullOrWhiteSpace(repo.Path))
            return false;

        var normalizedRepoPath = NormalizeLocalPath(repo.Path);
        if (string.Equals(normalizedRepoPath, normalizedGitRoot, StringComparison.OrdinalIgnoreCase))
            return true;

        if (IsSubdirectory(normalizedRepoPath, normalizedGitRoot))
            return true;

        if (!Path.IsPathRooted(repo.Path))
        {
            var combined = NormalizeLocalPath(Path.Combine(normalizedGitRoot, repo.Path));
            if (IsSubdirectory(combined, normalizedGitRoot))
                return true;
        }

        try
        {
            var resolvedRoot = GitHelper.ResolveGitRoot(repo.Path);
            if (resolvedRoot != null && string.Equals(NormalizeLocalPath(resolvedRoot), normalizedGitRoot, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch
        {
            // Ignore any errors if path does not exist
        }

        return false;
    }

    private static List<string> GetProjectSubdirectories(ProjectConfig project, string normalizedGitRoot)
    {
        var subdirs = new List<string>();

        void AddSubdir(string? sub)
        {
            if (string.IsNullOrWhiteSpace(sub)) return;

            string normalized;
            if (Path.IsPathRooted(sub))
            {
                normalized = NormalizeLocalPath(sub);
            }
            else
            {
                normalized = NormalizeLocalPath(Path.Combine(normalizedGitRoot, sub));
            }

            if (!subdirs.Any(s => string.Equals(s, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                subdirs.Add(normalized);
            }
        }

        AddSubdir(project.Subdirectory);

        var metaSub = project.GetMeta("subdirectory") ?? project.GetMeta("subfolder");
        AddSubdir(metaSub);

        foreach (var repo in project.Repos)
        {
            if (!RepoBelongsToGitRoot(repo, normalizedGitRoot))
                continue;

            AddSubdir(repo.Subdirectory);

            var normRepoPath = NormalizeLocalPath(repo.Path);
            if (IsSubdirectory(normRepoPath, normalizedGitRoot))
            {
                AddSubdir(normRepoPath);
            }
            else if (!Path.IsPathRooted(repo.Path))
            {
                var combined = NormalizeLocalPath(Path.Combine(normalizedGitRoot, repo.Path));
                if (IsSubdirectory(combined, normalizedGitRoot))
                {
                    AddSubdir(combined);
                }
            }
        }

        return subdirs;
    }

    public static bool IsSameOrSubdirectory(string path, string parent)
    {
        var normalizedPath = NormalizeLocalPath(path);
        var normalizedParent = NormalizeLocalPath(parent);

        if (string.Equals(normalizedPath, normalizedParent, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsSubdirectory(normalizedPath, normalizedParent);
    }

    public static bool IsSubdirectory(string path, string parent)
    {
        var normalizedPath = NormalizeLocalPath(path);
        var normalizedParent = NormalizeLocalPath(parent);

        if (string.Equals(normalizedPath, normalizedParent, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parentWithSeparator = normalizedParent.EndsWith(Path.DirectorySeparatorChar) || normalizedParent.EndsWith(Path.AltDirectorySeparatorChar)
            ? normalizedParent
            : normalizedParent + Path.DirectorySeparatorChar;

        return normalizedPath.StartsWith(parentWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeLocalPath(string path)
    {
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(path);
            var full = Path.GetFullPath(expanded);
            var trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.IsNullOrEmpty(trimmed) ? full : trimmed;
        }
        catch
        {
            var trimmed = path.TrimEnd('/', '\\');
            return string.IsNullOrEmpty(trimmed) ? path : trimmed;
        }
    }
}
