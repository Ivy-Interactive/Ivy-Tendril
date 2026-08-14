using System;
using System.IO;

namespace Ivy.Tendril.Helpers;

public static class ProjectPathHelper
{
    public static string GetProjectRoot(string tendrilHome, string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName)) return Path.Combine(tendrilHome, "Projects");
        var sanitized = InputSanitizer.SanitizeProjectName(projectName);
        return Path.Combine(tendrilHome, "Projects", sanitized);
    }

    public static string GetReposDir(string tendrilHome, string projectName)
        => Path.Combine(GetProjectRoot(tendrilHome, projectName), "Repos");

    public static string GetRepoPath(string tendrilHome, string projectName, string owner, string repoName)
    {
        var ownerSanitized = string.IsNullOrWhiteSpace(owner) ? "default" : InputSanitizer.SanitizeProjectName(owner);
        var repoSanitized = string.IsNullOrWhiteSpace(repoName) ? "repo" : InputSanitizer.SanitizeProjectName(repoName);
        return Path.Combine(GetReposDir(tendrilHome, projectName), ownerSanitized, repoSanitized);
    }

    public static string GetSkillsDir(string tendrilHome, string projectName)
        => Path.Combine(GetProjectRoot(tendrilHome, projectName), "Skills");

    public static string GetMcpDir(string tendrilHome, string projectName)
        => Path.Combine(GetProjectRoot(tendrilHome, projectName), "MCP");

    public static string GetMemoryDir(string tendrilHome, string projectName)
        => Path.Combine(GetProjectRoot(tendrilHome, projectName), "Memory");

    public static void EnsureProjectDirectories(string tendrilHome, string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName)) return;

        Directory.CreateDirectory(GetProjectRoot(tendrilHome, projectName));
        Directory.CreateDirectory(GetReposDir(tendrilHome, projectName));
        Directory.CreateDirectory(GetSkillsDir(tendrilHome, projectName));
        Directory.CreateDirectory(GetMcpDir(tendrilHome, projectName));
        Directory.CreateDirectory(GetMemoryDir(tendrilHome, projectName));
    }

    public static void MoveProjectDirectory(string tendrilHome, string oldProjectName, string newProjectName)
    {
        if (string.IsNullOrWhiteSpace(oldProjectName) || string.IsNullOrWhiteSpace(newProjectName)) return;
        if (string.Equals(oldProjectName, newProjectName, StringComparison.OrdinalIgnoreCase)) return;

        var oldDir = GetProjectRoot(tendrilHome, oldProjectName);
        var newDir = GetProjectRoot(tendrilHome, newProjectName);

        if (Directory.Exists(oldDir) && !Directory.Exists(newDir))
        {
            var newParent = Path.GetDirectoryName(newDir);
            if (!string.IsNullOrEmpty(newParent)) Directory.CreateDirectory(newParent);
            Directory.Move(oldDir, newDir);
        }

        EnsureProjectDirectories(tendrilHome, newProjectName);
    }
}
