using System;
using System.Collections.Generic;

namespace Ivy.Tendril.Models;

public class MemoryFrontmatter
{
    public string Title { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public string? Updated { get; set; }
    public Dictionary<string, string> Targets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Relations { get; set; } = new();
}

public class MemoryNote
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string ProjectName { get; set; } = "global";
    public MemoryFrontmatter Frontmatter { get; set; } = new();
    public string Content { get; set; } = "";

    public string Title => !string.IsNullOrWhiteSpace(Frontmatter.Title) ? Frontmatter.Title : Name;
    public List<string> Tags => Frontmatter.Tags;
    public Dictionary<string, string> Targets => Frontmatter.Targets;
    public List<string> Relations => Frontmatter.Relations;
}

public class MemoryTargetStatus
{
    public string RelativePath { get; set; } = "";
    public string ExpectedHash { get; set; } = "";
    public string? CurrentHash { get; set; }
    public bool Exists { get; set; }
    public bool IsOutdated => Exists && !string.Equals(ExpectedHash, CurrentHash, StringComparison.OrdinalIgnoreCase);
}

public class VaultStatusInfo
{
    public string VaultPath { get; set; }
    public int TotalMemories { get; set; }
    public int OutdatedMemories { get; set; }
    public int BrokenLinks { get; set; }
    public int OrphanMemories { get; set; }
    public int IncompleteTemplates { get; set; }
    public HashSet<string> OutdatedNoteNames { get; set; }
    public string RawOutput { get; set; }

    public VaultStatusInfo(
        string vaultPath,
        int totalMemories,
        int outdatedMemories,
        int brokenLinks,
        int orphanMemories,
        int incompleteTemplates,
        HashSet<string> outdatedNoteNames,
        string rawOutput)
    {
        VaultPath = vaultPath;
        TotalMemories = totalMemories;
        OutdatedMemories = outdatedMemories;
        BrokenLinks = brokenLinks;
        OrphanMemories = orphanMemories;
        IncompleteTemplates = incompleteTemplates;
        OutdatedNoteNames = outdatedNoteNames;
        RawOutput = rawOutput;
    }
}
