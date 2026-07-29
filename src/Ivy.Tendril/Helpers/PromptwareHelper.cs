using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Helpers;

public static class PromptwareHelper
{
    public static string ResolvePromptwareFolder(string promptwareName, string? tendrilHome, string? promptwarePath = null)
    {
        if (!string.IsNullOrEmpty(promptwarePath))
        {
            var overrideFolder = Path.Combine(promptwarePath, promptwareName);
            if (File.Exists(Path.Combine(overrideFolder, "Program.md")))
                return overrideFolder;
        }

        var sourceRoot = ResolvePromptsRoot(tendrilHome);
        var sourceFolder = Path.Combine(sourceRoot, promptwareName);

        if (File.Exists(Path.Combine(sourceFolder, "Program.md")))
            return sourceFolder;

        if (string.IsNullOrEmpty(tendrilHome))
            tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        if (!string.IsNullOrEmpty(tendrilHome))
        {
            var deployedRoot = Path.Combine(tendrilHome, "Promptwares");
            var deployedFolder = Path.Combine(deployedRoot, promptwareName);
            if (File.Exists(Path.Combine(deployedFolder, "Program.md")))
                return deployedFolder;
        }

        return sourceFolder;
    }

    public static string ResolvePromptsRoot(string? tendrilHome = null)
    {
        var sourceRoot = Path.GetFullPath(
            Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "Promptwares"));

        if (Directory.Exists(sourceRoot))
            return sourceRoot;

        if (string.IsNullOrEmpty(tendrilHome))
            tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        if (!string.IsNullOrEmpty(tendrilHome))
        {
            var deployedRoot = Path.Combine(tendrilHome, "Promptwares");
            if (Directory.Exists(deployedRoot))
                return deployedRoot;
        }

        if (!string.IsNullOrEmpty(tendrilHome))
            return Path.Combine(tendrilHome, "Promptwares");

        return sourceRoot;
    }

    public static void RequireProgramFolder(string programFolder, string promptwareName, string? tendrilHome)
    {
        if (File.Exists(Path.Combine(programFolder, "Program.md")) || Directory.Exists(programFolder))
            return;

        var promptsRoot = ResolvePromptsRoot(tendrilHome);
        var available = Directory.Exists(promptsRoot)
            ? string.Join(", ", Directory.EnumerateDirectories(promptsRoot)
                .Where(d => File.Exists(Path.Combine(d, "Program.md")))
                .Select(Path.GetFileName)
                .OrderBy(n => n, StringComparer.Ordinal))
            : "";
        if (string.IsNullOrEmpty(available))
            available = "(none)";

        throw new FileNotFoundException(
            $"Promptware not found: {promptwareName}. Available promptwares: {available}",
            programFolder);
    }

    public static string? FindProjectNameForPath(string path, string tendrilHome)
    {
        try
        {
            var configPath = Path.Combine(tendrilHome, "config.yaml");
            if (!File.Exists(configPath)) return null;

            var yaml = File.ReadAllText(configPath);
            var settings = YamlHelper.Deserializer.Deserialize<TendrilSettings>(yaml);
            if (settings?.Projects != null)
            {
                var targetPath = Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
                foreach (var p in settings.Projects)
                {
                    if (p.Repos == null) continue;
                    foreach (var repo in p.Repos)
                    {
                        if (string.IsNullOrEmpty(repo.Path)) continue;
                        try
                        {
                            var fullRepoPath = Path.GetFullPath(repo.Path).Replace('\\', '/').TrimEnd('/');
                            if (targetPath.StartsWith(fullRepoPath, StringComparison.OrdinalIgnoreCase))
                            {
                                return p.Name;
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }
        return null;
    }

    public static string? FindGitRepositoryRoot(string startDir)
    {
        try
        {
            var dir = startDir;
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir, ".git")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch { }
        return null;
    }
}
