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
    public async Task GetCatalogAsync_WhenVaultEmpty_ListsLocalOnlyProjects()
    {
        var config = CreateConfig();
        var vaultService = new VaultService(config, NullLogger<VaultService>.Instance);

        var catalog = await vaultService.GetCatalogAsync();

        Assert.Single(catalog.Projects);
        var proj = catalog.Projects[0];
        Assert.Equal("LocalApp", proj.Name);
        Assert.Equal(VaultItemSyncStatus.LocalOnly, proj.SyncStatus);
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

        Assert.Equal(2, catalog.Projects.Count);

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
        Assert.True(config.Settings.Vault.TrackedProjects.ContainsKey("SharedService"));
        Assert.Equal("2026.08.22.150000", config.Settings.Vault.TrackedProjects["SharedService"].InstalledVersion);
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
}
