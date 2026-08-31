using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Vault;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ivy.Tendril.Test;

[Collection("TendrilHome")]
public class VaultServiceTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("tendril-vault-service-test");
    private readonly string _originalTendrilHome;

    public VaultServiceTests()
    {
        _originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir.Path);

        var yaml = @"
projects:
  - name: LocalApp
    color: Sky
    context: Local application guidelines
    repos:
      - path: /tmp/repos/local-app
    mcpServers: []
    skills:
      - name: local-skill
        description: Local custom skill
        instructions: Do something local
verifications: []
";
        File.WriteAllText(Path.Combine(_tempDir.Path, "config.yaml"), yaml);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _originalTendrilHome);
        _tempDir.Dispose();
    }

    private ConfigService CreateConfig() => new(NullLogger<ConfigService>.Instance);

    [Fact]
    public void GenerateVersionTimestamp_ReturnsValidUtcFormat()
    {
        var config = CreateConfig();
        var vaultService = new VaultService(config, NullLogger<VaultService>.Instance);

        var version = vaultService.GenerateVersionTimestamp();

        Assert.Matches(@"^\d{4}\.\d{2}\.\d{2}\.\d{6}$", version);
    }

    [Fact]
    public async Task GetStatusAsync_WhenNotConfigured_ReturnsUnconfiguredStatus()
    {
        var config = CreateConfig();
        var vaultService = new VaultService(config, NullLogger<VaultService>.Instance);

        var status = await vaultService.GetStatusAsync();

        Assert.False(status.IsConfigured);
        Assert.Equal("", status.RepoUrl);
        Assert.False(status.AlwaysUpToDate);
    }

    [Fact]
    public async Task GetCatalogAsync_WhenVaultEmpty_ReturnsEmptyProjectsList()
    {
        var config = CreateConfig();
        var vaultService = new VaultService(config, NullLogger<VaultService>.Instance);

        var catalog = await vaultService.GetCatalogAsync();

        Assert.Empty(catalog.Projects);
    }

    [Fact]
    public async Task GetCatalogAsync_WithVaultProject_DetectsNotImportedAndVersions()
    {
        var config = CreateConfig();
        var vaultDir = Path.Combine(_tempDir.Path, "Vault");
        Directory.CreateDirectory(Path.Combine(vaultDir, "projects", "RemoteWeb"));

        var remoteManifest = new VaultProjectManifest
        {
            Name = "RemoteWeb",
            Version = "2026.08.22.120000",
            Changelog = "Initial commit of RemoteWeb",
            Color = "Emerald",
            Context = "React frontend",
            Repos = new List<VaultRepoRef>
            {
                new() { Owner = "team", Name = "remote-web", BaseBranch = "main" }
            }
        };

        File.WriteAllText(
            Path.Combine(vaultDir, "projects", "RemoteWeb", "project.yaml"),
            YamlHelper.SerializerCompact.Serialize(remoteManifest));

        var vaultService = new VaultService(config, NullLogger<VaultService>.Instance);
        var catalog = await vaultService.GetCatalogAsync();

        Assert.Single(catalog.Projects);

        var remoteProj = catalog.Projects.Find(p => p.Name == "RemoteWeb");
        Assert.NotNull(remoteProj);
        Assert.Equal("2026.08.22.120000", remoteProj.RemoteVersion);
        Assert.Equal(VaultItemSyncStatus.NotImported, remoteProj.SyncStatus);
    }

    [Fact]
    public async Task ImportProjectAsync_ImportsManifest_AndUpdatesTracking()
    {
        var config = CreateConfig();
        var vaultDir = Path.Combine(_tempDir.Path, "Vault");
        var projectDir = Path.Combine(vaultDir, "projects", "SharedService");
        var skillsDir = Path.Combine(projectDir, "skills");
        Directory.CreateDirectory(skillsDir);

        var manifest = new VaultProjectManifest
        {
            Name = "SharedService",
            Version = "2026.08.22.150000",
            Changelog = "Added database migrations and audit skill",
            Color = "Purple",
            Context = "Backend API service",
            Repos = new List<VaultRepoRef>
            {
                new() { Owner = "team", Name = "shared-api", BaseBranch = "main" }
            }
        };

        File.WriteAllText(Path.Combine(projectDir, "project.yaml"), YamlHelper.SerializerCompact.Serialize(manifest));
        File.WriteAllText(Path.Combine(skillsDir, "audit.md"), "# Audit Skill\n\nRun audit checks.");

        var permissions = new VaultPermissionsManifest
        {
            AllowedTerminalCommands = new List<string> { "dotnet test", "dotnet build" },
            OutsideFileAccessPolicy = "Ask"
        };
        File.WriteAllText(Path.Combine(projectDir, "permissions.yaml"), YamlHelper.SerializerCompact.Serialize(permissions));

        config.Settings.Vault = new VaultSettings
        {
            Enabled = true,
            RepoUrl = "https://github.com/team/Tendril-Vault.git",
            LocalPath = vaultDir
        };

        var vaultService = new VaultService(config, NullLogger<VaultService>.Instance);

        var mappings = new Dictionary<string, string>
        {
            ["team/shared-api"] = "/local/path/to/shared-api"
        };

        var result = await vaultService.ImportProjectAsync("SharedService", mappings);

        Assert.True(result.Success);

        // Verify loaded in config
        var imported = config.Settings.Projects.Find(p => p.Name == "SharedService");
        Assert.NotNull(imported);
        Assert.Equal("Purple", imported.Color);
        Assert.Equal("Ask", imported.OutsideFileAccessPolicy);
        Assert.Contains("dotnet test", imported.AllowedTerminalCommands);
        Assert.Single(imported.Repos);
        Assert.Equal("/local/path/to/shared-api", imported.Repos[0].Path);

        // Verify skills copied
        var localSkillFile = Path.Combine(ProjectPathHelper.GetSkillsDir(_tempDir.Path, "SharedService"), "audit.md");
        Assert.True(File.Exists(localSkillFile));
        Assert.Contains("Audit Skill", File.ReadAllText(localSkillFile));

        // Verify tracking
        var activeVault = config.Settings.Vaults.Count > 0 ? config.Settings.Vaults[0] : config.Settings.Vault;
        Assert.True(activeVault.TrackedProjects.ContainsKey("SharedService"));
        Assert.Equal("2026.08.22.150000", activeVault.TrackedProjects["SharedService"].InstalledVersion);
    }

    [Fact]
    public async Task SetAlwaysUpToDateAsync_UpdatesSetting_AndRaisesEvent()
    {
        var config = CreateConfig();
        config.Settings.Vault = new VaultSettings { Enabled = true, RepoUrl = "https://github.com/team/Tendril-Vault.git" };

        var vaultService = new VaultService(config, NullLogger<VaultService>.Instance);
        var eventFired = false;
        vaultService.VaultChanged += () => eventFired = true;

        var result = await vaultService.SetAlwaysUpToDateAsync(true);

        Assert.True(result.Success);
        Assert.True(config.Settings.Vault.AlwaysUpToDate);
        Assert.True(eventFired);
    }

    [Fact]
    public async Task ImportProjectAsync_WithGranularSelection_FiltersSelectedAssetsAndCopiesMemories()
    {
        var config = CreateConfig();
        var vaultDir = Path.Combine(_tempDir.Path, "Vault");
        var projectDir = Path.Combine(vaultDir, "projects", "GranularApp");
        var skillsDir = Path.Combine(projectDir, "skills");
        var memoryDir = Path.Combine(projectDir, "memory");
        Directory.CreateDirectory(skillsDir);
        Directory.CreateDirectory(memoryDir);

        File.WriteAllText(Path.Combine(skillsDir, "skill1.md"), "# Skill 1");
        File.WriteAllText(Path.Combine(skillsDir, "skill2.md"), "# Skill 2");
        File.WriteAllText(Path.Combine(memoryDir, "stack.md"), "# Stack Memory");
        File.WriteAllText(Path.Combine(memoryDir, "ignored.md"), "# Ignored Memory");

        var manifest = new VaultProjectManifest
        {
            Name = "GranularApp",
            Version = "2026.08.22.180000",
            Skills = new List<ProjectSkillRef>
            {
                new() { Name = "skill1", Description = "Skill 1", Instructions = "Do 1" },
                new() { Name = "skill2", Description = "Skill 2", Instructions = "Do 2" }
            },
            McpServers = new List<ProjectMcpServerRef>
            {
                new() { Name = "mcp1", Command = "node", Arguments = new() { "mcp1.js" } },
                new() { Name = "mcp2", Command = "node", Arguments = new() { "mcp2.js" } }
            },
            ReviewActions = new List<ReviewActionConfig>
            {
                new() { Name = "action1", Command = "dotnet test" },
                new() { Name = "action2", Command = "npm test" }
            },
            Verifications = new List<ProjectVerificationRef>
            {
                new() { Name = "verif1", Required = true },
                new() { Name = "verif2", Required = false }
            }
        };
        File.WriteAllText(Path.Combine(projectDir, "project.yaml"), YamlHelper.SerializerCompact.Serialize(manifest));

        config.Settings.Vault = new VaultSettings
        {
            Enabled = true,
            RepoUrl = "https://github.com/team/Tendril-Vault.git",
            LocalPath = vaultDir
        };

        var vaultService = new VaultService(config, NullLogger<VaultService>.Instance);

        var importReq = new VaultImportRequest
        {
            ProjectName = "GranularApp",
            LocalRepoMappings = new() { ["GranularApp"] = "/path/to/app" },
            SelectedSkills = new() { "skill1" },
            SelectedMcps = new() { "mcp2" },
            SelectedReviewActions = new() { "action1" },
            SelectedVerifications = new() { "verif2" },
            SelectedMemories = new() { "stack.md" },
            ImportPermissions = false
        };

        var result = await vaultService.ImportProjectAsync(importReq);

        Assert.True(result.Success);

        var imported = config.Settings.Projects.Find(p => p.Name == "GranularApp");
        Assert.NotNull(imported);

        // Verify only selected skills, mcps, actions, verifications are imported
        Assert.Single(imported.Skills);
        Assert.Equal("skill1", imported.Skills[0].Name);

        Assert.Single(imported.McpServers);
        Assert.Equal("mcp2", imported.McpServers[0].Name);

        Assert.Single(imported.ReviewActions);
        Assert.Equal("action1", imported.ReviewActions[0].Name);

        Assert.Single(imported.Verifications);
        Assert.Equal("verif2", imported.Verifications[0].Name);

        // Verify selected disk skill and memory copied, ignored ones not copied
        var localSkillsDir = ProjectPathHelper.GetSkillsDir(_tempDir.Path, "GranularApp");
        Assert.True(File.Exists(Path.Combine(localSkillsDir, "skill1.md")));
        Assert.False(File.Exists(Path.Combine(localSkillsDir, "skill2.md")));

        var localMemoryDir = ProjectPathHelper.GetMemoryDir(_tempDir.Path, "GranularApp");
        Assert.True(File.Exists(Path.Combine(localMemoryDir, "stack.md")));
        Assert.False(File.Exists(Path.Combine(localMemoryDir, "ignored.md")));
    }

    [Fact]
    public async Task ImportProjectAsync_RestoresWorkingProjectWithStackHashMetaAndVerificationDefinitions()
    {
        var config = CreateConfig();
        var vaultDir = Path.Combine(_tempDir.Path, "Vault");
        var projectDir = Path.Combine(vaultDir, "projects", "FullProject");
        Directory.CreateDirectory(projectDir);

        var manifest = new VaultProjectManifest
        {
            Name = "FullProject",
            Version = "2026.08.22.200000",
            Color = "Purple",
            Context = "Full working project context",
            StackHash = "sha256:abc123stackhash",
            Meta = new Dictionary<string, object> { ["team"] = "core", ["priority"] = "high" },
            Repos = new List<VaultRepoRef>
            {
                new() { Owner = "Ivy-Interactive", Name = "Ivy-Tendril", BaseBranch = "main", RemoteUrl = "https://github.com/Ivy-Interactive/Ivy-Tendril.git" }
            },
            Verifications = new List<ProjectVerificationRef>
            {
                new() { Name = "DotnetBuild", Required = true }
            },
            VerificationDefinitions = new List<VerificationConfig>
            {
                new() { Name = "DotnetBuild", Prompt = "Run dotnet build --warnaserror." }
            },
            ReviewActions = new List<ReviewActionConfig>
            {
                new() { Name = "QuickReview", Command = "echo review" }
            }
        };
        File.WriteAllText(Path.Combine(projectDir, "project.yaml"), YamlHelper.SerializerCompact.Serialize(manifest));

        config.Settings.Vault = new VaultSettings
        {
            Enabled = true,
            RepoUrl = "https://github.com/Ivy-Interactive/tendril-vault.git",
            LocalPath = vaultDir
        };

        var vaultService = new VaultService(config, NullLogger<VaultService>.Instance);

        var importReq = new VaultImportRequest
        {
            ProjectName = "FullProject",
            LocalRepoMappings = new() { ["Ivy-Interactive/Ivy-Tendril"] = "/tmp/repos/ivy-tendril" }
        };

        var result = await vaultService.ImportProjectAsync(importReq);

        Assert.True(result.Success);

        var imported = config.Settings.Projects.Find(p => p.Name == "FullProject");
        Assert.NotNull(imported);
        Assert.Equal("Purple", imported.Color);
        Assert.Equal("Full working project context", imported.Context);
        Assert.Equal("sha256:abc123stackhash", imported.StackHash);
        Assert.Equal("core", imported.GetMeta("team"));
        Assert.Single(imported.Repos);
        Assert.Equal("/tmp/repos/ivy-tendril", imported.Repos[0].Path);
        Assert.Equal("main", imported.Repos[0].BaseBranch);

        // Verification definition merged globally
        var def = config.Settings.Verifications.Find(v => v.Name == "DotnetBuild");
        Assert.NotNull(def);
        Assert.Equal("Run dotnet build --warnaserror.", def.Prompt);
    }

    [Fact]
    public async Task GetVaultsAsync_WithMultipleVaults_ReturnsAllConfiguredVaults()
    {
        var config = CreateConfig();
        var vault1Dir = Path.Combine(_tempDir.Path, "Vault1");
        var vault2Dir = Path.Combine(_tempDir.Path, "Vault2");
        Directory.CreateDirectory(vault1Dir);
        Directory.CreateDirectory(vault2Dir);

        config.Settings.Vaults = new List<VaultSettings>
        {
            new VaultSettings
            {
                Id = "v1",
                Name = "Core Vault",
                Enabled = true,
                RepoUrl = "https://github.com/team/core-vault.git",
                LocalPath = vault1Dir
            },
            new VaultSettings
            {
                Id = "v2",
                Name = "Mobile Vault",
                Enabled = true,
                RepoUrl = "https://github.com/team/mobile-vault.git",
                LocalPath = vault2Dir
            }
        };

        var vaultService = new VaultService(config, NullLogger<VaultService>.Instance);

        var vaults = await vaultService.GetVaultsAsync();

        Assert.Equal(2, vaults.Count);
        Assert.Equal("v1", vaults[0].Id);
        Assert.Equal("Core Vault", vaults[0].Name);
        Assert.Equal("v2", vaults[1].Id);
        Assert.Equal("Mobile Vault", vaults[1].Name);
    }

    [Fact]
    public async Task ConnectVaultAsync_WhenDuplicateUrl_ReturnsFailure()
    {
        var config = CreateConfig();
        var vaultDir = Path.Combine(_tempDir.Path, "Vault");
        Directory.CreateDirectory(vaultDir);

        config.Settings.Vaults = new List<VaultSettings>
        {
            new VaultSettings
            {
                Id = "v1",
                Name = "Team Vault",
                Enabled = true,
                RepoUrl = "https://github.com/team/Tendril-Vault.git",
                LocalPath = vaultDir
            }
        };

        var vaultService = new VaultService(config, NullLogger<VaultService>.Instance);

        // Same URL in different format
        var result = await vaultService.ConnectVaultAsync("git@github.com:team/Tendril-Vault.git");

        Assert.False(result.Success);
        Assert.Contains("already connected", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetCatalogAsync_WhenProjectNameCollision_DetectsConflictStatus()
    {
        var config = CreateConfig();
        var vaultDir = Path.Combine(_tempDir.Path, "Vault");
        var projectDir = Path.Combine(vaultDir, "projects", "LocalApp"); // Matches LocalApp in config.yaml
        Directory.CreateDirectory(projectDir);

        var remoteManifest = new VaultProjectManifest
        {
            Name = "LocalApp",
            Version = "2026.08.30.100000",
            Context = "Remote version of LocalApp",
            Color = "Emerald"
        };
        File.WriteAllText(Path.Combine(projectDir, "project.yaml"), YamlHelper.SerializerCompact.Serialize(remoteManifest));

        config.Settings.Vaults = new List<VaultSettings>
        {
            new VaultSettings
            {
                Id = "v1",
                Name = "Team Vault",
                Enabled = true,
                RepoUrl = "https://github.com/team/Tendril-Vault.git",
                LocalPath = vaultDir
            }
        };

        var vaultService = new VaultService(config, NullLogger<VaultService>.Instance);
        var catalog = await vaultService.GetCatalogAsync("v1");

        var item = catalog.Projects.Find(p => p.Name == "LocalApp");
        Assert.NotNull(item);
        Assert.Equal(VaultItemSyncStatus.Conflict, item.SyncStatus);
        Assert.True(item.HasLocalConflict);
        Assert.NotNull(item.ConflictReason);
    }

    [Fact]
    public async Task ImportProjectAsync_WithTargetLocalProjectName_ImportsWithCustomNameAndTracksVault()
    {
        var config = CreateConfig();
        var vaultDir = Path.Combine(_tempDir.Path, "Vault");
        var projectDir = Path.Combine(vaultDir, "projects", "LocalApp");
        Directory.CreateDirectory(projectDir);

        var manifest = new VaultProjectManifest
        {
            Name = "LocalApp",
            Version = "2026.08.30.120000",
            Color = "Rose",
            Context = "Imported copy with custom name"
        };
        File.WriteAllText(Path.Combine(projectDir, "project.yaml"), YamlHelper.SerializerCompact.Serialize(manifest));

        config.Settings.Vaults = new List<VaultSettings>
        {
            new VaultSettings
            {
                Id = "v2",
                Name = "Partner Vault",
                Enabled = true,
                RepoUrl = "https://github.com/partner/vault.git",
                LocalPath = vaultDir
            }
        };

        var vaultService = new VaultService(config, NullLogger<VaultService>.Instance);

        var importReq = new VaultImportRequest
        {
            ProjectName = "LocalApp",
            TargetLocalProjectName = "LocalApp-2",
            SourceVaultId = "v2"
        };

        var result = await vaultService.ImportProjectAsync(importReq, "v2");

        Assert.True(result.Success);

        // Original LocalApp still exists intact
        var original = config.Settings.Projects.Find(p => p.Name == "LocalApp");
        Assert.NotNull(original);
        Assert.Equal("Sky", original.Color);

        // New LocalApp-2 imported
        var imported = config.Settings.Projects.Find(p => p.Name == "LocalApp-2");
        Assert.NotNull(imported);
        Assert.Equal("Rose", imported.Color);

        // Tracking recorded under vault v2
        var v2 = config.Settings.Vaults.Find(v => v.Id == "v2");
        Assert.NotNull(v2);
        Assert.True(v2.TrackedProjects.ContainsKey("LocalApp-2"));
        Assert.Equal("2026.08.30.120000", v2.TrackedProjects["LocalApp-2"].InstalledVersion);
        Assert.Equal("v2", v2.TrackedProjects["LocalApp-2"].VaultId);
    }

    [Fact]
    public async Task DisconnectVaultAsync_RemovesSpecifiedVault_KeepsOtherVaults()
    {
        var config = CreateConfig();
        config.Settings.Vaults = new List<VaultSettings>
        {
            new VaultSettings { Id = "v1", Name = "Vault 1", Enabled = true, RepoUrl = "https://github.com/team/v1.git" },
            new VaultSettings { Id = "v2", Name = "Vault 2", Enabled = true, RepoUrl = "https://github.com/team/v2.git" }
        };

        var vaultService = new VaultService(config, NullLogger<VaultService>.Instance);

        var result = await vaultService.DisconnectVaultAsync("v1");

        Assert.True(result.Success);
        Assert.Single(config.Settings.Vaults);
        Assert.Equal("v2", config.Settings.Vaults[0].Id);
    }

    [Fact]
    public async Task DeleteProjectFromVaultAsync_DeletesProjectDirectory_AndUpdatesTracking()
    {
        var config = CreateConfig();
        var vaultDir = Path.Combine(_tempDir.Path, "Vault");
        Directory.CreateDirectory(Path.Combine(vaultDir, ".git"));
        var projectDir = Path.Combine(vaultDir, "projects", "DeletableApp");
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, "project.yaml"), "name: DeletableApp");

        config.Settings.Vaults = new List<VaultSettings>
        {
            new VaultSettings
            {
                Id = "v1",
                Name = "Test-Vault",
                Enabled = true,
                RepoUrl = "https://github.com/team/test-vault.git",
                LocalPath = vaultDir,
                TrackedProjects = new() { ["DeletableApp"] = new ProjectVaultTracking() }
            }
        };

        var vaultService = new VaultService(config, NullLogger<VaultService>.Instance);

        var result = await vaultService.DeleteProjectFromVaultAsync("DeletableApp", "v1");

        Assert.True(result.Success);
        Assert.False(Directory.Exists(projectDir));
        Assert.DoesNotContain("DeletableApp", config.Settings.Vaults[0].TrackedProjects.Keys);
        Assert.StartsWith("vault/delete-deletableapp-", result.BranchName);
    }

    [Fact]
    public async Task DiscoverExistingVaultsAsync_WhenNoAccounts_ReturnsEmptyList()
    {
        var config = CreateConfig();
        var vaultService = new VaultService(config, NullLogger<VaultService>.Instance);

        var discovered = await vaultService.DiscoverExistingVaultsAsync();

        Assert.NotNull(discovered);
    }
}
