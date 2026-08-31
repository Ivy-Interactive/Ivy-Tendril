using System;
using System.Collections.Generic;

namespace Ivy.Tendril.Services.Vault;

public enum VaultItemSyncStatus
{
    NotImported,
    UpToDate,
    UpdateAvailable,
    LocalOnly,
    Modified,
    Conflict
}

public record GitHubAccountOption(string Login, string Type);

public record VaultManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "Tendril-Vault";
    public string Description { get; set; } = "Team shared configuration vault for Ivy Tendril";
    public string Version { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? UpdatedBy { get; set; }
}

public record VaultRepoRef
{
    public string Owner { get; set; } = "";
    public string Name { get; set; } = "";
    public string? BaseBranch { get; set; }
    public string? RemoteUrl { get; set; }
}

public record VaultProjectManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? UpdatedBy { get; set; }
    public string Changelog { get; set; } = "";
    public string Color { get; set; } = "Blue";
    public string Context { get; set; } = "";
    public string? StackHash { get; set; }
    public Dictionary<string, object> Meta { get; set; } = new();
    public List<VaultRepoRef> Repos { get; set; } = new();
    public List<ProjectVerificationRef> Verifications { get; set; } = new();
    public List<VerificationConfig> VerificationDefinitions { get; set; } = new();
    public List<ReviewActionConfig> ReviewActions { get; set; } = new();
    public List<PromptwareHookConfig> Hooks { get; set; } = new();
    public List<string> BuildDependencies { get; set; } = new();
    public List<ProjectMcpServerRef> McpServers { get; set; } = new();
    public List<ProjectSkillRef> Skills { get; set; } = new();

    public string SecurityPreset { get; set; } = "Custom";
    public string OutsideFileAccessPolicy { get; set; } = "Allow";
    public string TerminalAutoExecution { get; set; } = "AlwaysProceed";
    public string SandboxMode { get; set; } = "InheritGeneral";
    public string AutoImplementPlans { get; set; } = "InheritGeneral";
}

public record VaultPermissionsManifest
{
    public List<FileAccessRuleConfig> FilePermissions { get; set; } = new();
    public List<NetworkAccessRuleConfig> NetworkAccessRules { get; set; } = new();
    public List<string> AllowedTerminalCommands { get; set; } = new();
    public string OutsideFileAccessPolicy { get; set; } = "Allow";
    public string TerminalAutoExecution { get; set; } = "AlwaysProceed";
    public string SandboxMode { get; set; } = "InheritGeneral";
}

public record VaultCatalogItem
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Color { get; set; } = "Blue";
    public string? StackHash { get; set; }
    public string? LocalVersion { get; set; }
    public string RemoteVersion { get; set; } = "";
    public string? LatestChangelog { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public int ReposCount { get; set; }
    public int SkillsCount { get; set; }
    public int McpsCount { get; set; }
    public int MemoriesCount { get; set; }
    public int ReviewActionsCount { get; set; }
    public int VerificationsCount { get; set; }
    public List<string> SkillNames { get; set; } = new();
    public List<string> McpServerNames { get; set; } = new();
    public List<string> MemoryFileNames { get; set; } = new();
    public List<string> ReviewActionNames { get; set; } = new();
    public List<string> VerificationNames { get; set; } = new();
    public VaultItemSyncStatus SyncStatus { get; set; }
    public List<VaultRepoRef> Repos { get; set; } = new();
    public bool HasLocalConflict { get; set; }
    public string? ConflictReason { get; set; }
    public string? LinkedVaultId { get; set; }
    public string? SourceVaultId { get; set; }
    public string? SourceVaultName { get; set; }
}

public record VaultCatalog
{
    public VaultManifest? Manifest { get; set; }
    public List<VaultCatalogItem> Projects { get; set; } = new();
    public List<string> GlobalSkills { get; set; } = new();
    public List<string> GlobalMcps { get; set; } = new();
}

public record VaultStatus
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsConfigured { get; set; }
    public string RepoUrl { get; set; } = "";
    public string LocalPath { get; set; } = "";
    public string CurrentBranch { get; set; } = "main";
    public string? LatestCommit { get; set; }
    public int CommitsAhead { get; set; }
    public int CommitsBehind { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public bool AlwaysUpToDate { get; set; }
}

public record VaultExportRequest
{
    public string? TargetVaultId { get; set; }
    public List<string> ProjectNames { get; set; } = new();
    public string Version { get; set; } = "";
    public string Changelog { get; set; } = "";
    public string PrTitle { get; set; } = "";
    public string PrBody { get; set; } = "";
    public List<string> Reviewers { get; set; } = new();
    public Dictionary<string, List<string>> SelectedSkills { get; set; } = new();
    public Dictionary<string, List<string>> SelectedMcps { get; set; } = new();
    public Dictionary<string, List<string>> SelectedMemories { get; set; } = new();
    public Dictionary<string, List<string>> SelectedReviewActions { get; set; } = new();
    public Dictionary<string, List<string>> SelectedVerifications { get; set; } = new();
    public Dictionary<string, bool> SyncPermissions { get; set; } = new();
}

public record VaultImportRequest
{
    public string? SourceVaultId { get; set; }
    public string ProjectName { get; set; } = "";
    public string? TargetLocalProjectName { get; set; }
    public Dictionary<string, string> LocalRepoMappings { get; set; } = new();
    public List<string>? SelectedSkills { get; set; }
    public List<string>? SelectedMcps { get; set; }
    public List<string>? SelectedMemories { get; set; }
    public List<string>? SelectedReviewActions { get; set; }
    public List<string>? SelectedVerifications { get; set; }
    public bool ImportPermissions { get; set; } = true;
}

public record VaultPrResult(bool Success, string? PrUrl = null, string? BranchName = null, string? ErrorMessage = null);

public record VaultResult(bool Success, string Message, string? ErrorMessage = null);

public record VaultSyncResult(bool Success, int UpdatedProjectsCount = 0, string Message = "", string? ErrorMessage = null);
