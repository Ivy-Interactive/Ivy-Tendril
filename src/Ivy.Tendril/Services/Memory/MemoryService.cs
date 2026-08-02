using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using YamlDotNet.Serialization;

namespace Ivy.Tendril.Services.Memory;

public class MemoryService : IMemoryService
{
    private static readonly Regex WikiLinkRegex = new(@"\[\[([^\]\|]+)(?:\|[^\]]+)?\]\]", RegexOptions.Compiled);

    public string ResolveVaultPath(string? workspaceDir = null, string? projectName = null)
    {
        workspaceDir ??= Directory.GetCurrentDirectory();
        return Path.Combine(workspaceDir, ".brainwares");
    }

    public VaultStatusInfo GetStatus(string? workspaceDir = null, string? projectName = null)
    {
        workspaceDir ??= Directory.GetCurrentDirectory();
        var vaultPath = ResolveVaultPath(workspaceDir, projectName);
        var memoriesDir = GetMemoriesDirectory(vaultPath, workspaceDir, projectName);

        var notes = LoadAllNotes(memoriesDir, workspaceDir, projectName);
        var noteLookup = notes.ToDictionary(n => n.Name, n => n, StringComparer.OrdinalIgnoreCase);

        var totalMemories = notes.Count;
        var outdatedMemoriesCount = 0;
        var brokenLinksCount = 0;
        var orphanMemoriesCount = 0;
        var incompleteTemplatesCount = 0;

        var outdatedNoteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        sb.AppendLine($"Vault path: \"{vaultPath}\"");

        var outgoingCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var incomingCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var note in notes)
        {
            outgoingCount[note.Name] = 0;
            incomingCount[note.Name] = 0;
        }

        foreach (var note in notes)
        {
            var isOutdatedNote = false;
            var noteHeaderWritten = false;

            void EnsureHeader()
            {
                if (!noteHeaderWritten)
                {
                    sb.AppendLine($"Memory: {note.Name}");
                    noteHeaderWritten = true;
                }
            }

            // Check target code files
            if (note.Targets.Count > 0)
            {
                foreach (var (relPath, expectedHash) in note.Targets)
                {
                    var normRel = relPath.Replace('\\', '/');
                    var fullPath = Path.Combine(workspaceDir, normRel);
                    if (!File.Exists(fullPath))
                    {
                        EnsureHeader();
                        sb.AppendLine($"  [MISSING CODE] {normRel} (file deleted or moved)");
                        isOutdatedNote = true;
                    }
                    else
                    {
                        var currentHash = ComputeSha256(fullPath);
                        if (!string.Equals(expectedHash, currentHash, StringComparison.OrdinalIgnoreCase))
                        {
                            EnsureHeader();
                            sb.AppendLine($"  [OUTDATED CODE] {normRel} (hash mismatch)");
                            isOutdatedNote = true;
                        }
                    }
                }
            }

            if (isOutdatedNote)
            {
                outdatedMemoriesCount++;
                outdatedNoteNames.Add(note.Name);
            }

            // Check relations in frontmatter
            foreach (var targetRelation in note.Relations)
            {
                outgoingCount[note.Name] = outgoingCount.GetValueOrDefault(note.Name) + 1;
                if (noteLookup.ContainsKey(targetRelation))
                {
                    incomingCount[targetRelation] = incomingCount.GetValueOrDefault(targetRelation) + 1;
                }
                else
                {
                    EnsureHeader();
                    sb.AppendLine($"  [BROKEN LINK] Frontmatter relation target not found: {targetRelation}");
                    brokenLinksCount++;
                }
            }

            // Check Obsidian wiki-links in body
            var matches = WikiLinkRegex.Matches(note.Content);
            foreach (Match match in matches)
            {
                var targetName = match.Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(targetName)) continue;

                outgoingCount[note.Name] = outgoingCount.GetValueOrDefault(note.Name) + 1;
                if (noteLookup.ContainsKey(targetName))
                {
                    incomingCount[targetName] = incomingCount.GetValueOrDefault(targetName) + 1;
                }
                else
                {
                    EnsureHeader();
                    sb.AppendLine($"  [BROKEN LINK] Wiki-link target not found: [[{targetName}]]");
                    brokenLinksCount++;
                }
            }

            // Check incomplete templates
            if (note.Content.Contains("TODO:") || note.Content.Contains("<placeholder>"))
            {
                incompleteTemplatesCount++;
            }
        }

        // Check orphan memories
        foreach (var note in notes)
        {
            if (outgoingCount.GetValueOrDefault(note.Name) == 0 &&
                incomingCount.GetValueOrDefault(note.Name) == 0 &&
                note.Targets.Count == 0)
            {
                orphanMemoriesCount++;
            }
        }

        sb.AppendLine($"Total memories: {totalMemories}");
        sb.AppendLine($"Outdated memories: {outdatedMemoriesCount}");
        sb.AppendLine($"Broken wiki-links: {brokenLinksCount}");
        sb.AppendLine($"Orphan memories: {orphanMemoriesCount}");
        sb.AppendLine($"Incomplete templates: {incompleteTemplatesCount}");

        return new VaultStatusInfo(
            vaultPath,
            totalMemories,
            outdatedMemoriesCount,
            brokenLinksCount,
            orphanMemoriesCount,
            incompleteTemplatesCount,
            outdatedNoteNames,
            sb.ToString().TrimEnd());
    }

    public IEnumerable<MemoryNote> ListMemories(string? workspaceDir = null, string? projectName = null)
    {
        workspaceDir ??= Directory.GetCurrentDirectory();
        var vaultPath = ResolveVaultPath(workspaceDir, projectName);
        var memoriesDir = GetMemoriesDirectory(vaultPath, workspaceDir, projectName);
        return LoadAllNotes(memoriesDir, workspaceDir, projectName);
    }

    public MemoryNote? ReadMemory(string noteName, string? workspaceDir = null, string? projectName = null)
    {
        workspaceDir ??= Directory.GetCurrentDirectory();
        var normalizedName = NormalizeNoteName(noteName);
        var memories = ListMemories(workspaceDir, projectName);

        var note = memories.FirstOrDefault(n => string.Equals(n.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
        if (note != null) return note;

        // Search global fallback
        var globalNotes = LoadAllNotes(Path.Combine(ResolveGlobalVaultPath(), "memories", "global"), workspaceDir, "global");
        return globalNotes.FirstOrDefault(n => string.Equals(n.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
    }

    public MemoryNote AddMemory(
        string name,
        string? title = null,
        IEnumerable<string>? tags = null,
        string? content = null,
        string? workspaceDir = null,
        string? projectName = null)
    {
        workspaceDir ??= Directory.GetCurrentDirectory();
        var vaultPath = ResolveVaultPath(workspaceDir, projectName);
        var memoriesDir = GetMemoriesDirectory(vaultPath, workspaceDir, projectName);
        Directory.CreateDirectory(memoriesDir);

        var normalizedName = NormalizeNoteName(name);
        var fileName = normalizedName.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? normalizedName : $"{normalizedName}.md";
        var filePath = Path.Combine(memoriesDir, fileName);

        var frontmatter = new MemoryFrontmatter
        {
            Title = title ?? normalizedName,
            Tags = tags?.ToList() ?? new List<string>(),
            Updated = DateTime.UtcNow.ToString("o")
        };

        var noteContent = content ?? $"# {frontmatter.Title}\n\n";
        SaveNoteToFile(filePath, frontmatter, noteContent);

        return new MemoryNote
        {
            Name = normalizedName,
            Path = filePath,
            ProjectName = projectName ?? "global",
            Frontmatter = frontmatter,
            Content = noteContent
        };
    }

    public void WriteMemory(string noteName, string content, string? workspaceDir = null, string? projectName = null)
    {
        workspaceDir ??= Directory.GetCurrentDirectory();
        var existing = ReadMemory(noteName, workspaceDir, projectName);
        if (existing == null)
        {
            AddMemory(noteName, content: content, workspaceDir: workspaceDir, projectName: projectName);
            return;
        }

        var (frontmatter, rawBody) = SplitFrontmatterAndBody(content);
        if (frontmatter == null)
        {
            frontmatter = existing.Frontmatter;
            rawBody = content;
        }

        frontmatter.Updated = DateTime.UtcNow.ToString("o");
        SaveNoteToFile(existing.Path, frontmatter, rawBody);
    }

    public void LinkFile(string noteName, string relativeFilePath, string? workspaceDir = null, string? projectName = null)
    {
        workspaceDir ??= Directory.GetCurrentDirectory();
        var note = ReadMemory(noteName, workspaceDir, projectName);
        if (note == null)
        {
            note = AddMemory(noteName, workspaceDir: workspaceDir, projectName: projectName);
        }

        var fullPath = Path.Combine(workspaceDir, relativeFilePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Cannot link non-existent code file: {relativeFilePath}", fullPath);
        }

        var normRelPath = relativeFilePath.Replace('\\', '/');
        var hash = ComputeSha256(fullPath);
        note.Frontmatter.Targets[normRelPath] = hash;
        note.Frontmatter.Updated = DateTime.UtcNow.ToString("o");

        SaveNoteToFile(note.Path, note.Frontmatter, note.Content);
    }

    public void UpdateMemory(string noteName, string? workspaceDir = null, string? projectName = null)
    {
        workspaceDir ??= Directory.GetCurrentDirectory();
        var note = ReadMemory(noteName, workspaceDir, projectName);
        if (note == null)
        {
            throw new FileNotFoundException($"Memory note not found: {noteName}");
        }

        if (note.Targets.Count > 0)
        {
            var updatedTargets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (relPath, _) in note.Targets)
            {
                var fullPath = Path.Combine(workspaceDir, relPath);
                if (File.Exists(fullPath))
                {
                    updatedTargets[relPath] = ComputeSha256(fullPath);
                }
            }
            note.Frontmatter.Targets = updatedTargets;
        }

        note.Frontmatter.Updated = DateTime.UtcNow.ToString("o");
        SaveNoteToFile(note.Path, note.Frontmatter, note.Content);
    }

    public void RelateMemories(string sourceNote, string targetNote, string? workspaceDir = null, string? projectName = null)
    {
        workspaceDir ??= Directory.GetCurrentDirectory();
        var note = ReadMemory(sourceNote, workspaceDir, projectName);
        if (note == null)
        {
            throw new FileNotFoundException($"Source memory note not found: {sourceNote}");
        }

        var normTarget = NormalizeNoteName(targetNote);
        if (!note.Frontmatter.Relations.Contains(normTarget, StringComparer.OrdinalIgnoreCase))
        {
            note.Frontmatter.Relations.Add(normTarget);
            note.Frontmatter.Updated = DateTime.UtcNow.ToString("o");
            SaveNoteToFile(note.Path, note.Frontmatter, note.Content);
        }
    }

    public void DeleteMemory(string noteName, string? workspaceDir = null, string? projectName = null)
    {
        workspaceDir ??= Directory.GetCurrentDirectory();
        var note = ReadMemory(noteName, workspaceDir, projectName);
        if (note != null && File.Exists(note.Path))
        {
            File.Delete(note.Path);
        }
    }

    public IEnumerable<MemoryNote> QueryMemories(string keyword, string? workspaceDir = null, string? projectName = null)
    {
        var memories = ListMemories(workspaceDir, projectName);
        if (string.IsNullOrWhiteSpace(keyword)) return memories;

        return memories.Where(n =>
            n.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            n.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            n.Tags.Any(t => t.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
            n.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    public string GetRulesMarkdown(string? workspaceDir = null, string? projectName = null)
    {
        var memories = ListMemories(workspaceDir, projectName).ToList();
        if (memories.Count == 0) return "";

        var sb = new StringBuilder();
        sb.AppendLine("## Codebase Memories & Vault Context");
        sb.AppendLine();

        foreach (var note in memories)
        {
            sb.AppendLine($"### Note: {note.Name} ({note.Title})");
            if (note.Tags.Count > 0)
            {
                sb.AppendLine($"Tags: {string.Join(", ", note.Tags)}");
            }
            if (note.Targets.Count > 0)
            {
                sb.AppendLine($"Tracked files: {string.Join(", ", note.Targets.Keys)}");
            }
            sb.AppendLine();
            sb.AppendLine(note.Content.Trim());
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string NormalizeNoteName(string name)
    {
        var clean = name.Trim();
        if (clean.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring(0, clean.Length - 3);
        }
        return clean;
    }

    private static string GetMemoriesDirectory(string vaultPath, string workspaceDir, string? projectName)
    {
        if (vaultPath.EndsWith(".brainwares", StringComparison.OrdinalIgnoreCase))
        {
            return vaultPath;
        }

        projectName ??= PromptwareHelper.FindProjectNameForPath(workspaceDir, ResolveTendrilHome()) ?? "global";
        return Path.Combine(vaultPath, "memories", projectName);
    }

    private static string ResolveGlobalVaultPath()
    {
        return Path.Combine(ResolveTendrilHome(), "Promptwares");
    }

    private static string ResolveTendrilHome()
    {
        return Environment.GetEnvironmentVariable("TENDRIL_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tendril");
    }

    private static List<MemoryNote> LoadAllNotes(string memoriesDir, string workspaceDir, string? projectName)
    {
        var list = new List<MemoryNote>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void ScanDirectory(string dir, string defaultProject)
        {
            if (!Directory.Exists(dir)) return;
            try
            {
                var files = Directory.GetFiles(dir, "*.md", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    if (!seenPaths.Add(file)) continue;
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (name.StartsWith(".")) continue;

                    var raw = File.ReadAllText(file);
                    var (frontmatter, body) = SplitFrontmatterAndBody(raw);

                    list.Add(new MemoryNote
                    {
                        Name = name,
                        Path = file,
                        ProjectName = defaultProject,
                        Frontmatter = frontmatter ?? new MemoryFrontmatter { Title = name },
                        Content = body
                    });
                }
            }
            catch
            {
                // Ignore directory scan errors
            }
        }

        // 1. Primary workspace vault directory (.brainwares)
        ScanDirectory(memoriesDir, projectName ?? "workspace");

        // 2. Promptware memories in TENDRIL_HOME/Promptwares/*/Memory
        var tendrilHome = ResolveTendrilHome();
        var promptwaresDir = Path.Combine(tendrilHome, "Promptwares");
        if (Directory.Exists(promptwaresDir))
        {
            foreach (var pwDir in Directory.GetDirectories(promptwaresDir))
            {
                var pwName = Path.GetFileName(pwDir);
                var memFolder = Path.Combine(pwDir, "Memory");
                ScanDirectory(memFolder, pwName);
            }
            ScanDirectory(Path.Combine(promptwaresDir, "memories"), "global");
        }

        // 3. Built-in Promptwares directory
        var baseAppDir = AppDomain.CurrentDomain.BaseDirectory;
        var builtinPwDirs = new[]
        {
            Path.Combine(baseAppDir, "Promptwares"),
            Path.Combine(workspaceDir, "src", "Ivy.Tendril", "Promptwares"),
            Path.Combine(workspaceDir, "Promptwares")
        };

        foreach (var builtinDir in builtinPwDirs)
        {
            if (!Directory.Exists(builtinDir)) continue;
            foreach (var pwDir in Directory.GetDirectories(builtinDir))
            {
                var pwName = Path.GetFileName(pwDir);
                if (pwName.Equals("memories", StringComparison.OrdinalIgnoreCase)) continue;
                var memFolder = Path.Combine(pwDir, "Memory");
                ScanDirectory(memFolder, pwName);
            }
        }

        return list.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static (MemoryFrontmatter? frontmatter, string body) SplitFrontmatterAndBody(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, "");

        var normalized = text.Replace("\r\n", "\n");
        if (!normalized.StartsWith("---\n"))
        {
            return (null, text);
        }

        var endIdx = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (endIdx < 0)
        {
            return (null, text);
        }

        var yamlText = normalized.Substring(4, endIdx - 4);
        var body = normalized.Substring(endIdx + 5);

        try
        {
            var frontmatter = YamlHelper.Deserializer.Deserialize<MemoryFrontmatter>(yamlText);
            return (frontmatter ?? new MemoryFrontmatter(), body);
        }
        catch
        {
            return (null, body);
        }
    }

    private static void SaveNoteToFile(string filePath, MemoryFrontmatter frontmatter, string body)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var yaml = YamlHelper.Serializer.Serialize(frontmatter);
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.Append(yaml.TrimEnd());
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine(body.TrimStart());

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    private static string ComputeSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var bytes = sha256.ComputeHash(stream);
        return Convert.ToHexStringLower(bytes);
    }
}
