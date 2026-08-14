using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Helpers;

public record DiscoveredMcpServer(
    string Name,
    string Command,
    List<string> Arguments,
    Dictionary<string, string> Environment,
    string SourceFilePath
);

public record DiscoveredSkill(
    string Name,
    string Description,
    string? Instructions,
    string SkillFolderPath,
    string RelativePath
);

public static class RepoAssetScanner
{
    private static readonly string[] McpConfigFileNames =
    [
        ".mcp.json",
        "mcp.json",
        ".mcp_config.json",
        "mcp_config.json",
        Path.Combine(".vscode", "mcp.json"),
        Path.Combine(".cursor", "mcp.json"),
        Path.Combine(".gemini", "mcp_config.json"),
        Path.Combine(".agents", "mcp_config.json"),
        Path.Combine(".claude", "mcp.json"),
        "claude_desktop_config.json"
    ];

    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj", "dist", "build", ".vs", ".idea", ".venv", "venv", ".cache"
    };

    public static (string? LocalPath, string? Error) ResolveAndPrepareRepoPath(string repoPathOrUrl, string tendrilHome)
    {
        if (string.IsNullOrWhiteSpace(repoPathOrUrl))
            return (null, "Repository path or URL cannot be empty.");

        var expanded = VariableExpansion.ExpandVariables(repoPathOrUrl.Trim(), tendrilHome ?? "");

        if (Directory.Exists(expanded))
            return (Path.GetFullPath(expanded), null);

        var kind = RepoPathValidator.Classify(expanded);
        if (kind == RepoPathKind.HttpUrl || kind == RepoPathKind.SshUrl)
        {
            var repoName = RepoPathValidator.ExtractRepoName(expanded) ?? "remote-repo";
            var cacheRoot = string.IsNullOrEmpty(tendrilHome)
                ? Path.Combine(Path.GetTempPath(), "tendril-imports")
                : Path.Combine(tendrilHome, "Cache", "Imports");
            var targetDir = Path.Combine(cacheRoot, InputSanitizer.SanitizeProjectName(repoName));

            try
            {
                if (Directory.Exists(targetDir))
                {
                    var pullPsi = GitHelper.MakeGitStartInfo("pull --depth 1", targetDir);
                    using var pullProc = Process.Start(pullPsi);
                    if (pullProc != null && pullProc.WaitForExit(20000) && pullProc.ExitCode == 0)
                    {
                        return (targetDir, null);
                    }

                    try { Directory.Delete(targetDir, recursive: true); } catch { }
                }

                Directory.CreateDirectory(cacheRoot);
                var clonePsi = GitHelper.MakeGitStartInfo($"clone --depth 1 \"{expanded}\" \"{targetDir}\"");
                using var cloneProc = Process.Start(clonePsi);
                if (cloneProc == null)
                    return (null, "Failed to start git clone process.");

                var stdErr = cloneProc.StandardError.ReadToEnd();
                cloneProc.WaitForExit(60000);

                if (cloneProc.ExitCode == 0 && Directory.Exists(targetDir))
                    return (targetDir, null);

                return (null, $"Git clone failed: {stdErr.Trim()}");
            }
            catch (Exception ex)
            {
                return (null, $"Failed to clone repository: {ex.Message}");
            }
        }

        if (RepoPathValidator.IsLocalPath(expanded))
            return (null, $"Directory does not exist: {expanded}");

        return (null, $"Invalid repository path or URL: {expanded}");
    }

    public static List<DiscoveredMcpServer> ScanMcpServers(string repoPath)
    {
        var results = new List<DiscoveredMcpServer>();
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
            return results;

        foreach (var relativeFile in McpConfigFileNames)
        {
            var fullPath = Path.Combine(repoPath, relativeFile);
            if (!File.Exists(fullPath)) continue;

            try
            {
                var text = File.ReadAllText(fullPath);
                using var doc = JsonDocument.Parse(text);

                JsonElement serversElement = default;
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("mcpServers", out var mcpProp) && mcpProp.ValueKind == JsonValueKind.Object)
                        serversElement = mcpProp;
                    else if (doc.RootElement.TryGetProperty("servers", out var srvProp) && srvProp.ValueKind == JsonValueKind.Object)
                        serversElement = srvProp;
                    else
                        serversElement = doc.RootElement;
                }

                if (serversElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in serversElement.EnumerateObject())
                    {
                        var sName = prop.Name;
                        if (prop.Value.ValueKind != JsonValueKind.Object) continue;

                        var cmd = prop.Value.TryGetProperty("command", out var cElem) ? cElem.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(cmd)) continue;

                        var args = new List<string>();
                        if (prop.Value.TryGetProperty("args", out var aElem) && aElem.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var a in aElem.EnumerateArray())
                                if (a.GetString() is { } aStr) args.Add(aStr);
                        }

                        var env = new Dictionary<string, string>();
                        if (prop.Value.TryGetProperty("env", out var eElem) && eElem.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var envProp in eElem.EnumerateObject())
                                if (envProp.Value.GetString() is { } eVal) env[envProp.Name] = eVal;
                        }

                        if (!results.Any(r => r.Name.Equals(sName, StringComparison.OrdinalIgnoreCase)))
                        {
                            results.Add(new DiscoveredMcpServer(sName, cmd, args, env, relativeFile));
                        }
                    }
                }
            }
            catch
            {
                // Ignore malformed JSON files during scanning
            }
        }

        return results;
    }

    public static List<DiscoveredSkill> ScanSkills(string repoPath)
    {
        var results = new List<DiscoveredSkill>();
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
            return results;

        try
        {
            ScanSkillsRecursive(repoPath, repoPath, results, maxDepth: 6, currentDepth: 0);
        }
        catch
        {
            // Ignore scan directory traversal errors
        }

        return results;
    }

    private static void ScanSkillsRecursive(
        string rootPath,
        string currentPath,
        List<DiscoveredSkill> results,
        int maxDepth,
        int currentDepth)
    {
        if (currentDepth > maxDepth) return;

        var skillFile = Directory.GetFiles(currentPath, "*.md")
            .FirstOrDefault(f => Path.GetFileName(f).Equals("SKILL.md", StringComparison.OrdinalIgnoreCase));

        if (skillFile != null)
        {
            var skillDir = Path.GetDirectoryName(skillFile) ?? currentPath;
            var dirName = Path.GetFileName(skillDir);
            var (name, desc, inst) = ParseSkillMarkdown(skillFile, dirName);

            var relativePath = Path.GetRelativePath(rootPath, skillDir);
            if (!results.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(new DiscoveredSkill(name, desc, inst, skillDir, relativePath));
            }
        }

        foreach (var subDir in Directory.GetDirectories(currentPath))
        {
            var dirName = Path.GetFileName(subDir);
            if (IgnoredDirectories.Contains(dirName)) continue;
            ScanSkillsRecursive(rootPath, subDir, results, maxDepth, currentDepth + 1);
        }
    }

    public static (string Name, string Description, string? Instructions) ParseSkillMarkdown(string skillFilePath, string fallbackName)
    {
        try
        {
            var content = File.ReadAllText(skillFilePath);
            var name = fallbackName;
            var description = "";
            string? instructions = null;

            if (content.StartsWith("---"))
            {
                var endIndex = content.IndexOf("---", 3, StringComparison.Ordinal);
                if (endIndex > 0)
                {
                    var yaml = content.Substring(3, endIndex - 3);
                    instructions = content[(endIndex + 3)..].Trim();

                    var nameMatch = Regex.Match(yaml, @"(?m)^\s*name\s*:\s*['""]?(.*?)['""]?\s*$");
                    if (nameMatch.Success && !string.IsNullOrWhiteSpace(nameMatch.Groups[1].Value))
                        name = nameMatch.Groups[1].Value.Trim();

                    var descMatch = Regex.Match(yaml, @"(?m)^\s*description\s*:\s*['""]?(.*?)['""]?\s*$");
                    if (descMatch.Success && !string.IsNullOrWhiteSpace(descMatch.Groups[1].Value))
                        description = descMatch.Groups[1].Value.Trim();
                }
                else
                {
                    instructions = content;
                }
            }
            else
            {
                instructions = content;
            }

            if (string.IsNullOrWhiteSpace(description) && !string.IsNullOrWhiteSpace(instructions))
            {
                var firstLine = instructions.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault(l => !l.StartsWith('#'));
                if (firstLine != null)
                    description = firstLine.Length > 100 ? firstLine[..97] + "..." : firstLine;
            }

            if (string.IsNullOrWhiteSpace(description))
                description = $"Custom skill {name}";

            return (name, description, instructions);
        }
        catch
        {
            return (fallbackName, $"Custom skill {fallbackName}", null);
        }
    }

    public static ProjectSkillRef ImportSkillToProject(
        string tendrilHome,
        string projectName,
        DiscoveredSkill skill,
        bool copyFiles = true)
    {
        var targetDir = Path.Combine(ProjectPathHelper.GetSkillsDir(tendrilHome, projectName), skill.Name);

        if (copyFiles && Directory.Exists(skill.SkillFolderPath))
        {
            CopyDirectory(skill.SkillFolderPath, targetDir);
        }
        else if (!Directory.Exists(targetDir) && !string.IsNullOrWhiteSpace(skill.Instructions))
        {
            Directory.CreateDirectory(targetDir);
            var skillMdPath = Path.Combine(targetDir, "SKILL.md");
            File.WriteAllText(skillMdPath, $"---\nname: {skill.Name}\ndescription: {skill.Description}\n---\n\n{skill.Instructions}");
        }

        return new ProjectSkillRef
        {
            Name = skill.Name,
            Description = skill.Description,
            Instructions = skill.Instructions,
            Path = $"%PROJECT_ROOT%/Skills/{skill.Name}",
            Disabled = false
        };
    }

    public static ProjectMcpServerRef ImportMcpServer(DiscoveredMcpServer server)
    {
        return new ProjectMcpServerRef
        {
            Name = server.Name,
            Command = server.Command,
            Arguments = new List<string>(server.Arguments),
            Environment = new Dictionary<string, string>(server.Environment),
            Disabled = false
        };
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            var destFile = Path.Combine(targetDir, fileName);
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(subDir);
            if (IgnoredDirectories.Contains(dirName)) continue;
            var destSubDir = Path.Combine(targetDir, dirName);
            CopyDirectory(subDir, destSubDir);
        }
    }
}
