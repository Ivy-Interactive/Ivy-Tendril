using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Ivy.Helpers;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;

namespace Ivy.Tendril.Services.Vault;

public class VaultService : IVaultService
{
    private readonly IConfigService _config;
    private readonly ILogger<VaultService> _logger;

    public event Action? VaultChanged;

    public VaultService(IConfigService config, ILogger<VaultService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public string GenerateVersionTimestamp()
    {
        return DateTime.UtcNow.ToString("yyyy.MM.dd.HHmmss");
    }

    private string GetVaultDirectory()
    {
        if (_config.Settings.Vault != null && !string.IsNullOrEmpty(_config.Settings.Vault.LocalPath))
        {
            return _config.Settings.Vault.LocalPath;
        }

        return Path.Combine(_config.TendrilHome, "Vault");
    }

    public async Task<VaultStatus> GetStatusAsync()
    {
        var settings = _config.Settings.Vault;
        var vaultDir = GetVaultDirectory();

        if (settings == null || !settings.Enabled || string.IsNullOrEmpty(settings.RepoUrl) || !Directory.Exists(vaultDir))
        {
            return new VaultStatus
            {
                IsConfigured = false,
                RepoUrl = settings?.RepoUrl ?? "",
                LocalPath = vaultDir,
                AlwaysUpToDate = settings?.AlwaysUpToDate ?? false,
                LastSyncedAt = settings?.LastSyncedAt
            };
        }

        var (branch, _) = await RunGitCommandAsync(vaultDir, "rev-parse --abbrev-ref HEAD");
        var (commit, _) = await RunGitCommandAsync(vaultDir, "rev-parse --short HEAD");

        int ahead = 0;
        int behind = 0;
        try
        {
            var (revCount, _) = await RunGitCommandAsync(vaultDir, "rev-list --left-right --count origin/main...HEAD");
            if (!string.IsNullOrWhiteSpace(revCount))
            {
                var parts = revCount.Trim().Split('\t', ' ');
                if (parts.Length == 2)
                {
                    int.TryParse(parts[0], out behind);
                    int.TryParse(parts[1], out ahead);
                }
            }
        }
        catch
        {
            // origin/main may not exist yet or offline
        }

        return new VaultStatus
        {
            IsConfigured = true,
            RepoUrl = settings.RepoUrl,
            LocalPath = vaultDir,
            CurrentBranch = branch?.Trim() ?? "main",
            LatestCommit = commit?.Trim(),
            CommitsAhead = ahead,
            CommitsBehind = behind,
            LastSyncedAt = settings.LastSyncedAt,
            AlwaysUpToDate = settings.AlwaysUpToDate
        };
    }

    public Task<VaultCatalog> GetCatalogAsync()
    {
        var vaultDir = GetVaultDirectory();
        var catalog = new VaultCatalog();

        if (!Directory.Exists(vaultDir))
        {
            // Populate with local projects as LocalOnly
            foreach (var localProj in _config.Settings.Projects)
            {
                catalog.Projects.Add(new VaultCatalogItem
                {
                    Name = localProj.Name,
                    Description = localProj.Context,
                    Color = localProj.Color,
                    LocalVersion = GetTrackedVersion(localProj.Name),
                    RemoteVersion = "",
                    SyncStatus = VaultItemSyncStatus.LocalOnly,
                    Repos = localProj.Repos.Select(r => r.Path).ToList(),
                    ReposCount = localProj.Repos.Count,
                    SkillsCount = localProj.Skills.Count,
                    McpsCount = localProj.McpServers.Count,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
            return Task.FromResult(catalog);
        }

        // Read vault.yaml
        var manifestPath = Path.Combine(vaultDir, "vault.yaml");
        if (File.Exists(manifestPath))
        {
            try
            {
                var yaml = File.ReadAllText(manifestPath);
                catalog.Manifest = YamlHelper.Deserializer.Deserialize<VaultManifest>(yaml);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read vault.yaml");
            }
        }

        var vaultProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectsDir = Path.Combine(vaultDir, "projects");

        if (Directory.Exists(projectsDir))
        {
            foreach (var projDir in Directory.GetDirectories(projectsDir))
            {
                var projName = Path.GetFileName(projDir);
                vaultProjects.Add(projName);

                var projManifestPath = Path.Combine(projDir, "project.yaml");
                VaultProjectManifest? manifest = null;
                if (File.Exists(projManifestPath))
                {
                    try
                    {
                        var yaml = File.ReadAllText(projManifestPath);
                        manifest = YamlHelper.Deserializer.Deserialize<VaultProjectManifest>(yaml);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse project.yaml for {Project}", projName);
                    }
                }

                var skillsDir = Path.Combine(projDir, "skills");
                var mcpsDir = Path.Combine(projDir, "mcps");

                var skillsCount = Directory.Exists(skillsDir) ? Directory.GetFiles(skillsDir).Length : (manifest?.Skills.Count ?? 0);
                var mcpsCount = Directory.Exists(mcpsDir) ? Directory.GetFiles(mcpsDir).Length : (manifest?.McpServers.Count ?? 0);
                var remoteVersion = manifest?.Version ?? "";
                var localVersion = GetTrackedVersion(projName);
                var isImported = _config.Settings.Projects.Any(p => p.Name.Equals(projName, StringComparison.OrdinalIgnoreCase));

                VaultItemSyncStatus syncStatus;
                if (!isImported)
                {
                    syncStatus = VaultItemSyncStatus.NotImported;
                }
                else if (string.IsNullOrEmpty(localVersion) || string.IsNullOrEmpty(remoteVersion))
                {
                    syncStatus = VaultItemSyncStatus.UpToDate;
                }
                else if (string.Equals(localVersion, remoteVersion, StringComparison.OrdinalIgnoreCase))
                {
                    syncStatus = VaultItemSyncStatus.UpToDate;
                }
                else
                {
                    syncStatus = VaultItemSyncStatus.UpdateAvailable;
                }

                catalog.Projects.Add(new VaultCatalogItem
                {
                    Name = projName,
                    Description = manifest?.Context ?? "",
                    Color = manifest?.Color ?? "Blue",
                    LocalVersion = localVersion,
                    RemoteVersion = remoteVersion,
                    LatestChangelog = manifest?.Changelog,
                    UpdatedAt = manifest?.UpdatedAt ?? DateTimeOffset.UtcNow,
                    UpdatedBy = manifest?.UpdatedBy,
                    ReposCount = manifest?.Repos.Count ?? 0,
                    SkillsCount = skillsCount,
                    McpsCount = mcpsCount,
                    SyncStatus = syncStatus,
                    Repos = manifest?.Repos.Select(r => $"{r.Owner}/{r.Name}").ToList() ?? new()
                });
            }
        }

        // Add local-only projects
        foreach (var localProj in _config.Settings.Projects)
        {
            if (!vaultProjects.Contains(localProj.Name))
            {
                catalog.Projects.Add(new VaultCatalogItem
                {
                    Name = localProj.Name,
                    Description = localProj.Context,
                    Color = localProj.Color,
                    LocalVersion = GetTrackedVersion(localProj.Name),
                    RemoteVersion = "",
                    SyncStatus = VaultItemSyncStatus.LocalOnly,
                    Repos = localProj.Repos.Select(r => r.Path).ToList(),
                    ReposCount = localProj.Repos.Count,
                    SkillsCount = localProj.Skills.Count,
                    McpsCount = localProj.McpServers.Count,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
        }

        return Task.FromResult(catalog);
    }

    public async Task<VaultResult> CreateVaultRepoAsync(string repoName, bool isPrivate = true, string? org = null)
    {
        var vaultDir = GetVaultDirectory();
        var targetRepo = !string.IsNullOrWhiteSpace(org) ? $"{org}/{repoName}" : repoName;
        var visibilityFlag = isPrivate ? "--private" : "--public";

        var (createOut, createErr) = await RunGhCliAsync($"repo create {targetRepo} {visibilityFlag} --confirm");
        if (createErr != null && !createErr.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            return new VaultResult(false, "Failed to create GitHub repository", createErr);
        }

        var (urlOut, urlErr) = await RunGhCliAsync($"repo view {targetRepo} --json url -q .url");
        var repoUrl = urlOut?.Trim();
        if (string.IsNullOrWhiteSpace(repoUrl))
        {
            repoUrl = $"https://github.com/{targetRepo}.git";
        }

        if (Directory.Exists(vaultDir))
        {
            Directory.Delete(vaultDir, true);
        }

        Directory.CreateDirectory(vaultDir);
        await RunGitCommandAsync(vaultDir, "init -b main");
        await RunGitCommandAsync(vaultDir, $"remote add origin {repoUrl}");

        // Initialize structure
        var manifest = new VaultManifest
        {
            Name = repoName,
            Version = GenerateVersionTimestamp(),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        File.WriteAllText(Path.Combine(vaultDir, "vault.yaml"), YamlHelper.SerializerCompact.Serialize(manifest));
        File.WriteAllText(Path.Combine(vaultDir, ".gitignore"), ".DS_Store\n*.local.yaml\n");
        Directory.CreateDirectory(Path.Combine(vaultDir, "projects"));
        Directory.CreateDirectory(Path.Combine(vaultDir, "global", "skills"));

        await RunGitCommandAsync(vaultDir, "add -A");
        await RunGitCommandAsync(vaultDir, "commit -m \"Initial Tendril Vault setup\"");
        await RunGitCommandAsync(vaultDir, "push -u origin main");

        _config.Settings.Vault = new VaultSettings
        {
            Enabled = true,
            RepoUrl = repoUrl,
            LocalPath = vaultDir,
            LastSyncedAt = DateTimeOffset.UtcNow,
            AlwaysUpToDate = false
        };
        _config.SaveSettings();
        VaultChanged?.Invoke();

        return new VaultResult(true, $"Created and connected vault repository '{targetRepo}'");
    }

    public async Task<VaultResult> ConnectVaultAsync(string repoUrl)
    {
        if (string.IsNullOrWhiteSpace(repoUrl))
            return new VaultResult(false, "Repository URL cannot be empty", "Empty URL");

        var vaultDir = GetVaultDirectory();
        if (Directory.Exists(vaultDir))
        {
            Directory.Delete(vaultDir, true);
        }

        var parent = Path.GetDirectoryName(vaultDir);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

        var (cloneOut, cloneErr) = await RunGitCommandAsync(parent ?? Path.GetTempPath(), $"clone {repoUrl} {vaultDir}");
        if (cloneErr != null && !Directory.Exists(Path.Combine(vaultDir, ".git")))
        {
            return new VaultResult(false, "Failed to clone vault repository", cloneErr);
        }

        _config.Settings.Vault = new VaultSettings
        {
            Enabled = true,
            RepoUrl = repoUrl,
            LocalPath = vaultDir,
            LastSyncedAt = DateTimeOffset.UtcNow,
            AlwaysUpToDate = false
        };
        _config.SaveSettings();
        VaultChanged?.Invoke();

        return new VaultResult(true, "Successfully connected to vault repository");
    }

    public Task<VaultResult> DisconnectVaultAsync()
    {
        if (_config.Settings.Vault != null)
        {
            _config.Settings.Vault.Enabled = false;
            _config.SaveSettings();
        }
        VaultChanged?.Invoke();
        return Task.FromResult(new VaultResult(true, "Disconnected from vault"));
    }

    public Task<VaultResult> SetAlwaysUpToDateAsync(bool alwaysUpToDate)
    {
        if (_config.Settings.Vault != null)
        {
            _config.Settings.Vault.AlwaysUpToDate = alwaysUpToDate;
            _config.SaveSettings();
        }
        VaultChanged?.Invoke();
        return Task.FromResult(new VaultResult(true, $"Auto-sync is now {(alwaysUpToDate ? "enabled" : "disabled")}"));
    }

    public async Task<VaultPrResult> PushAndCreatePrAsync(VaultExportRequest request)
    {
        var vaultDir = GetVaultDirectory();
        if (!Directory.Exists(Path.Combine(vaultDir, ".git")))
        {
            return new VaultPrResult(false, ErrorMessage: "Vault repository is not initialized locally.");
        }

        var version = !string.IsNullOrWhiteSpace(request.Version)
            ? request.Version
            : GenerateVersionTimestamp();

        var timestampId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var branchName = $"vault/update-{timestampId}";

        // Fast forward main
        await RunGitCommandAsync(vaultDir, "checkout main");
        await RunGitCommandAsync(vaultDir, "pull origin main");
        await RunGitCommandAsync(vaultDir, $"checkout -B {branchName}");

        var exportedProjects = new List<string>();

        foreach (var projName in request.ProjectNames)
        {
            var proj = _config.Settings.Projects.FirstOrDefault(p => p.Name.Equals(projName, StringComparison.OrdinalIgnoreCase));
            if (proj == null) continue;

            var projDir = Path.Combine(vaultDir, "projects", proj.Name);
            Directory.CreateDirectory(projDir);

            // 1. Export project.yaml
            var projectManifest = new VaultProjectManifest
            {
                Name = proj.Name,
                Version = version,
                UpdatedAt = DateTimeOffset.UtcNow,
                Changelog = request.Changelog,
                Color = proj.Color,
                Context = proj.Context,
                Repos = proj.Repos.Select(r =>
                {
                    var owner = "default";
                    var name = Path.GetFileName(r.Path.TrimEnd('/', '\\'));
                    return new VaultRepoRef { Owner = owner, Name = name, BaseBranch = r.BaseBranch };
                }).ToList(),
                Verifications = proj.Verifications,
                ReviewActions = proj.ReviewActions,
                Hooks = proj.Hooks,
                BuildDependencies = proj.BuildDependencies,
                McpServers = VaultSecretSanitizer.SanitizeMcpServers(proj.McpServers),
                Skills = proj.Skills,
                SecurityPreset = proj.SecurityPreset,
                OutsideFileAccessPolicy = proj.OutsideFileAccessPolicy,
                TerminalAutoExecution = proj.TerminalAutoExecution,
                SandboxMode = proj.SandboxMode,
                AutoImplementPlans = proj.AutoImplementPlans
            };

            var projYaml = YamlHelper.SerializerCompact.Serialize(projectManifest);
            File.WriteAllText(Path.Combine(projDir, "project.yaml"), projYaml);

            // 2. Export permissions.yaml
            var permManifest = new VaultPermissionsManifest
            {
                FilePermissions = proj.FilePermissions,
                NetworkAccessRules = proj.NetworkAccessRules,
                AllowedTerminalCommands = proj.AllowedTerminalCommands,
                OutsideFileAccessPolicy = proj.OutsideFileAccessPolicy,
                TerminalAutoExecution = proj.TerminalAutoExecution,
                SandboxMode = proj.SandboxMode
            };
            var permYaml = YamlHelper.SerializerCompact.Serialize(permManifest);
            File.WriteAllText(Path.Combine(projDir, "permissions.yaml"), permYaml);

            // 3. Export disk skills
            var localSkillsDir = ProjectPathHelper.GetSkillsDir(_config.TendrilHome, proj.Name);
            var vaultSkillsDir = Path.Combine(projDir, "skills");
            if (Directory.Exists(localSkillsDir))
            {
                Directory.CreateDirectory(vaultSkillsDir);
                foreach (var file in Directory.GetFiles(localSkillsDir, "*.md"))
                {
                    File.Copy(file, Path.Combine(vaultSkillsDir, Path.GetFileName(file)), true);
                }
            }

            // Update local tracking
            if (_config.Settings.Vault != null)
            {
                _config.Settings.Vault.TrackedProjects[proj.Name] = new ProjectVaultTracking
                {
                    InstalledVersion = version,
                    InstalledAt = DateTimeOffset.UtcNow,
                    LocalRepoPaths = proj.Repos.ToDictionary(r => Path.GetFileName(r.Path.TrimEnd('/', '\\')), r => r.Path)
                };
            }

            exportedProjects.Add(proj.Name);
        }

        // Update root vault.yaml
        var rootManifest = new VaultManifest
        {
            Name = _config.Settings.Vault?.RepoUrl != null ? Path.GetFileNameWithoutExtension(_config.Settings.Vault.RepoUrl) : "Tendril-Vault",
            Version = version,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        File.WriteAllText(Path.Combine(vaultDir, "vault.yaml"), YamlHelper.SerializerCompact.Serialize(rootManifest));

        await RunGitCommandAsync(vaultDir, "add -A");
        var commitMsg = !string.IsNullOrWhiteSpace(request.Changelog)
            ? $"feat(vault): update {string.Join(", ", exportedProjects)} (v{version})\n\n{request.Changelog}"
            : $"feat(vault): update {string.Join(", ", exportedProjects)} (v{version})";

        await RunGitCommandAsync(vaultDir, $"commit -m \"{commitMsg.Replace("\"", "\\\"")}\"");
        var (pushOut, pushErr) = await RunGitCommandAsync(vaultDir, $"push -u origin {branchName}");

        // Create Pull Request via gh CLI
        var prTitle = !string.IsNullOrWhiteSpace(request.PrTitle)
            ? request.PrTitle
            : $"Update {string.Join(", ", exportedProjects)} to v{version}";

        var prBody = !string.IsNullOrWhiteSpace(request.PrBody)
            ? request.PrBody
            : $"### Vault Version Update: v{version}\n\n**Changelog:**\n{request.Changelog}\n\n**Projects:**\n{string.Join("\n", exportedProjects.Select(p => $"- {p}"))}";

        var ghPrCmd = $"pr create --title \"{prTitle.Replace("\"", "\\\"")}\" --body \"{prBody.Replace("\"", "\\\"")}\" --head \"{branchName}\"";
        if (request.Reviewers.Count > 0)
        {
            ghPrCmd += $" --reviewer \"{string.Join(",", request.Reviewers)}\"";
        }

        var (prOut, prErr) = await RunGhCliAsync(ghPrCmd, vaultDir);
        var prUrl = prOut?.Trim();

        _config.SaveSettings();
        VaultChanged?.Invoke();

        return new VaultPrResult(true, PrUrl: prUrl, BranchName: branchName);
    }

    public async Task<VaultResult> ImportProjectAsync(string projectName, Dictionary<string, string> localRepoMappings)
    {
        var vaultDir = GetVaultDirectory();
        var projDir = Path.Combine(vaultDir, "projects", projectName);

        if (!Directory.Exists(projDir))
        {
            return new VaultResult(false, $"Project '{projectName}' was not found in vault.", "Directory not found");
        }

        var projManifestPath = Path.Combine(projDir, "project.yaml");
        if (!File.Exists(projManifestPath))
        {
            return new VaultResult(false, $"Project manifest for '{projectName}' missing.", "project.yaml missing");
        }

        var yaml = await File.ReadAllTextAsync(projManifestPath);
        var manifest = YamlHelper.Deserializer.Deserialize<VaultProjectManifest>(yaml);

        var repos = new List<RepoRef>();
        foreach (var r in manifest.Repos)
        {
            var key = $"{r.Owner}/{r.Name}";
            if (localRepoMappings.TryGetValue(key, out var path) || localRepoMappings.TryGetValue(r.Name, out path))
            {
                repos.Add(new RepoRef { Path = path, BaseBranch = r.BaseBranch });
            }
            else
            {
                // Fallback default path
                var fallbackPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "git", r.Name);
                repos.Add(new RepoRef { Path = fallbackPath, BaseBranch = r.BaseBranch });
            }
        }

        // Permissions
        var permManifestPath = Path.Combine(projDir, "permissions.yaml");
        VaultPermissionsManifest? permManifest = null;
        if (File.Exists(permManifestPath))
        {
            try
            {
                var permYaml = await File.ReadAllTextAsync(permManifestPath);
                permManifest = YamlHelper.Deserializer.Deserialize<VaultPermissionsManifest>(permYaml);
            }
            catch { }
        }

        var projectConfig = new ProjectConfig
        {
            Name = manifest.Name,
            Color = manifest.Color,
            Context = manifest.Context,
            Repos = repos,
            Verifications = manifest.Verifications,
            ReviewActions = manifest.ReviewActions,
            Hooks = manifest.Hooks,
            BuildDependencies = manifest.BuildDependencies,
            McpServers = manifest.McpServers,
            Skills = manifest.Skills,
            SecurityPreset = manifest.SecurityPreset,
            OutsideFileAccessPolicy = permManifest?.OutsideFileAccessPolicy ?? manifest.OutsideFileAccessPolicy,
            TerminalAutoExecution = permManifest?.TerminalAutoExecution ?? manifest.TerminalAutoExecution,
            SandboxMode = permManifest?.SandboxMode ?? manifest.SandboxMode,
            AutoImplementPlans = manifest.AutoImplementPlans,
            FilePermissions = permManifest?.FilePermissions ?? new(),
            NetworkAccessRules = permManifest?.NetworkAccessRules ?? new(),
            AllowedTerminalCommands = permManifest?.AllowedTerminalCommands ?? new()
        };

        // Merge into config.yaml
        var existingIdx = _config.Settings.Projects.FindIndex(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));
        if (existingIdx >= 0)
        {
            _config.Settings.Projects[existingIdx] = projectConfig;
        }
        else
        {
            _config.Settings.Projects.Add(projectConfig);
        }

        // Setup local project directories and copy skills
        ProjectPathHelper.EnsureProjectDirectories(_config.TendrilHome, projectName);
        var vaultSkillsDir = Path.Combine(projDir, "skills");
        var localSkillsDir = ProjectPathHelper.GetSkillsDir(_config.TendrilHome, projectName);

        if (Directory.Exists(vaultSkillsDir))
        {
            foreach (var file in Directory.GetFiles(vaultSkillsDir, "*.md"))
            {
                File.Copy(file, Path.Combine(localSkillsDir, Path.GetFileName(file)), true);
            }
        }

        // Update tracking
        if (_config.Settings.Vault != null)
        {
            _config.Settings.Vault.TrackedProjects[projectName] = new ProjectVaultTracking
            {
                InstalledVersion = manifest.Version,
                InstalledAt = DateTimeOffset.UtcNow,
                LocalRepoPaths = localRepoMappings
            };
        }

        _config.SaveSettings();
        VaultChanged?.Invoke();

        return new VaultResult(true, $"Successfully imported project '{projectName}' (v{manifest.Version})");
    }

    public async Task<VaultSyncResult> PullLatestAsync()
    {
        var vaultDir = GetVaultDirectory();
        if (!Directory.Exists(Path.Combine(vaultDir, ".git")))
        {
            return new VaultSyncResult(false, Message: "Vault is not configured or cloned locally.");
        }

        var (pullOut, pullErr) = await RunGitCommandAsync(vaultDir, "pull --rebase origin main");
        if (pullErr != null && pullErr.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            return new VaultSyncResult(false, Message: "Failed to pull latest vault changes", ErrorMessage: pullErr);
        }

        if (_config.Settings.Vault != null)
        {
            _config.Settings.Vault.LastSyncedAt = DateTimeOffset.UtcNow;
            _config.SaveSettings();
        }

        int updatedCount = 0;

        // If AlwaysUpToDate, auto-update tracked projects
        if (_config.Settings.Vault?.AlwaysUpToDate == true)
        {
            var catalog = await GetCatalogAsync();
            foreach (var item in catalog.Projects)
            {
                if (item.SyncStatus == VaultItemSyncStatus.UpdateAvailable)
                {
                    var tracking = _config.Settings.Vault.TrackedProjects.TryGetValue(item.Name, out var t) ? t : null;
                    var mappings = tracking?.LocalRepoPaths ?? new();
                    await ImportProjectAsync(item.Name, mappings);
                    updatedCount++;
                }
            }
        }

        VaultChanged?.Invoke();
        return new VaultSyncResult(true, UpdatedProjectsCount: updatedCount, Message: "Vault synchronized successfully");
    }

    private string? GetTrackedVersion(string projectName)
    {
        if (_config.Settings.Vault != null &&
            _config.Settings.Vault.TrackedProjects.TryGetValue(projectName, out var tracking))
        {
            return tracking.InstalledVersion;
        }

        return null;
    }

    private static async Task<(string? output, string? error)> RunGitCommandAsync(string workingDir, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var proc = Process.Start(psi);
            if (proc == null) return (null, "Failed to start git process");

            var stdout = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            return proc.ExitCode == 0 ? (stdout, null) : (stdout, stderr);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    private static async Task<(string? output, string? error)> RunGhCliAsync(string arguments, string? workingDir = null)
    {
        try
        {
            var psi = new ProcessStartInfo("gh", arguments)
            {
                WorkingDirectory = workingDir ?? Path.GetTempPath(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var proc = Process.Start(psi);
            if (proc == null) return (null, "GitHub CLI (gh) is not available.");

            var stdout = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            return proc.ExitCode == 0 ? (stdout, null) : (stdout, stderr);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }
}
