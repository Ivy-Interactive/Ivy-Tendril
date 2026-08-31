using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;

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

    private void EnsureVaultsInitialized()
    {
        var settings = _config.Settings;
        if (settings.Vaults == null)
        {
            settings.Vaults = new List<VaultSettings>();
        }

        // Auto-heal any corrupted RepoUrl that saved JSON error payload
        foreach (var v in settings.Vaults)
        {
            if (!string.IsNullOrWhiteSpace(v.RepoUrl) && v.RepoUrl.Trim().StartsWith("{"))
            {
                v.RepoUrl = $"https://github.com/{v.Name}.git";
            }
        }

        if (settings.Vaults.Count == 0 && settings.Vault != null && !string.IsNullOrEmpty(settings.Vault.RepoUrl))
        {
            if (settings.Vault.RepoUrl.Trim().StartsWith("{"))
            {
                settings.Vault.RepoUrl = $"https://github.com/{settings.Vault.Name}.git";
            }
            if (string.IsNullOrEmpty(settings.Vault.Id))
            {
                settings.Vault.Id = Guid.NewGuid().ToString("N")[..8];
            }
            if (string.IsNullOrEmpty(settings.Vault.Name))
            {
                settings.Vault.Name = ExtractRepoName(settings.Vault.RepoUrl);
            }
            settings.Vaults.Add(settings.Vault);
        }

        if (settings.Vaults.Count > 0)
        {
            foreach (var v in settings.Vaults)
            {
                if (string.IsNullOrEmpty(v.Id)) v.Id = Guid.NewGuid().ToString("N")[..8];
                if (string.IsNullOrEmpty(v.Name) || v.Name.Equals("Tendril-Vault", StringComparison.OrdinalIgnoreCase))
                {
                    var extracted = ExtractRepoName(v.RepoUrl);
                    if (!string.IsNullOrEmpty(extracted))
                    {
                        v.Name = extracted;
                    }
                }
            }

            if (settings.Vault == null || !settings.Vault.Enabled)
            {
                settings.Vault = settings.Vaults.FirstOrDefault(v => v.Enabled) ?? settings.Vaults[0];
            }
        }
    }

    private VaultSettings? GetVaultSettings(string? vaultId = null)
    {
        EnsureVaultsInitialized();
        var vaults = _config.Settings.Vaults;

        if (!string.IsNullOrEmpty(vaultId))
        {
            var found = vaults.FirstOrDefault(v =>
                v.Id.Equals(vaultId, StringComparison.OrdinalIgnoreCase) ||
                v.RepoUrl.Equals(vaultId, StringComparison.OrdinalIgnoreCase) ||
                v.Name.Equals(vaultId, StringComparison.OrdinalIgnoreCase));
            if (found != null) return found;
        }

        return vaults.FirstOrDefault(v => v.Enabled) ?? _config.Settings.Vault;
    }

    private string GetVaultDirectory(VaultSettings? vault)
    {
        if (vault != null && !string.IsNullOrEmpty(vault.LocalPath))
        {
            return vault.LocalPath;
        }

        if (vault != null && !string.IsNullOrEmpty(vault.Id))
        {
            return Path.Combine(_config.TendrilHome, "Vaults", vault.Id);
        }

        return Path.Combine(_config.TendrilHome, "Vault");
    }

    public static string NormalizeRepoUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        var trimmed = url.Trim().TrimEnd('/');
        if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        var scpMatch = Regex.Match(trimmed, @"^git@([^:]+):(.+)$");
        if (scpMatch.Success)
        {
            var host = scpMatch.Groups[1].Value;
            var path = scpMatch.Groups[2].Value;
            return $"https://{host}/{path}".ToLowerInvariant();
        }

        return trimmed.ToLowerInvariant();
    }

    public static string ExtractRepoName(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "Tendril-Vault";
        var trimmed = url.Trim().TrimEnd('/');
        if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        var scpMatch = Regex.Match(trimmed, @"^[\w\-]+@[\w\-.]+:([\w\-]+/[\w\-.]+)$");
        if (scpMatch.Success)
        {
            return scpMatch.Groups[1].Value;
        }

        var httpMatch = Regex.Match(trimmed, @"https?://[^/]+/([\w\-]+/[\w\-.]+)$");
        if (httpMatch.Success)
        {
            return httpMatch.Groups[1].Value;
        }

        var parts = trimmed.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && !parts[^2].Contains(':'))
        {
            return $"{parts[^2]}/{parts[^1]}";
        }
        if (parts.Length >= 1)
        {
            return parts[^1];
        }

        return "Tendril-Vault";
    }

    public async Task<List<VaultStatus>> GetVaultsAsync()
    {
        EnsureVaultsInitialized();
        var statuses = new List<VaultStatus>();

        foreach (var vault in _config.Settings.Vaults)
        {
            if (vault.Enabled)
            {
                var status = await GetStatusForVaultAsync(vault);
                statuses.Add(status);
            }
        }

        return statuses;
    }

    public async Task<VaultStatus> GetStatusAsync(string? vaultId = null)
    {
        var settings = GetVaultSettings(vaultId);
        return await GetStatusForVaultAsync(settings);
    }

    private async Task<VaultStatus> GetStatusForVaultAsync(VaultSettings? settings)
    {
        var vaultDir = GetVaultDirectory(settings);

        if (settings == null || !settings.Enabled || string.IsNullOrEmpty(settings.RepoUrl) || !Directory.Exists(vaultDir))
        {
            return new VaultStatus
            {
                Id = settings?.Id ?? "",
                Name = settings?.Name ?? (settings != null ? ExtractRepoName(settings.RepoUrl) : ""),
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

        var lastSynced = settings.LastSyncedAt;
        if ((!lastSynced.HasValue || lastSynced.Value.Year <= 1) && Directory.Exists(Path.Combine(vaultDir, ".git")))
        {
            var (commitDateOut, _) = await RunGitCommandAsync(vaultDir, "log -1 --format=%cI");
            if (DateTimeOffset.TryParse(commitDateOut?.Trim(), out var parsedDate) && parsedDate.Year > 1)
            {
                lastSynced = parsedDate;
                settings.LastSyncedAt = parsedDate;
                _config.SaveSettings();
            }
        }

        return new VaultStatus
        {
            Id = settings.Id,
            Name = settings.Name,
            IsConfigured = true,
            RepoUrl = settings.RepoUrl,
            LocalPath = vaultDir,
            CurrentBranch = branch?.Trim() ?? "main",
            LatestCommit = commit?.Trim(),
            CommitsAhead = ahead,
            CommitsBehind = behind,
            LastSyncedAt = lastSynced,
            AlwaysUpToDate = settings.AlwaysUpToDate
        };
    }

    public Task<VaultCatalog> GetCatalogAsync(string? vaultId = null)
    {
        var vault = GetVaultSettings(vaultId);
        var vaultDir = GetVaultDirectory(vault);
        var catalog = new VaultCatalog();

        if (!Directory.Exists(vaultDir))
        {
            return Task.FromResult(catalog);
        }

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
                var memoryDir = Path.Combine(projDir, "memory");

                var skillNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (manifest != null)
                {
                    foreach (var s in manifest.Skills) skillNames.Add(s.Name);
                }
                if (Directory.Exists(skillsDir))
                {
                    foreach (var f in Directory.GetFiles(skillsDir, "*.md"))
                        skillNames.Add(Path.GetFileNameWithoutExtension(f));
                }

                var mcpNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (manifest != null)
                {
                    foreach (var m in manifest.McpServers) mcpNames.Add(m.Name);
                }
                if (Directory.Exists(mcpsDir))
                {
                    foreach (var f in Directory.GetFiles(mcpsDir, "*.yaml"))
                        mcpNames.Add(Path.GetFileNameWithoutExtension(f));
                }

                var memoryNames = new List<string>();
                if (Directory.Exists(memoryDir))
                {
                    memoryNames = Directory.GetFiles(memoryDir, "*.md").Select(Path.GetFileName).OfType<string>().ToList();
                }

                var reviewActionNames = manifest?.ReviewActions.Select(r => r.Name).ToList() ?? new();
                var verificationNames = manifest?.Verifications.Select(v => v.Name).ToList() ?? new();

                var remoteVersion = manifest?.Version ?? "";
                var localVersion = GetTrackedVersion(projName, vault);
                var localMatchingProj = _config.Settings.Projects.FirstOrDefault(p => p.Name.Equals(projName, StringComparison.OrdinalIgnoreCase));
                var isImported = localMatchingProj != null;

                bool isTrackedToThisVault = false;
                if (vault != null && vault.TrackedProjects.TryGetValue(projName, out var tracking))
                {
                    isTrackedToThisVault = string.IsNullOrEmpty(tracking.VaultId) || tracking.VaultId.Equals(vault.Id, StringComparison.OrdinalIgnoreCase);
                }

                VaultItemSyncStatus syncStatus;
                bool hasConflict = false;
                string? conflictReason = null;

                if (!isImported)
                {
                    syncStatus = VaultItemSyncStatus.NotImported;
                }
                else if (!isTrackedToThisVault)
                {
                    // A project with this name already exists locally, but was not imported from this vault!
                    syncStatus = VaultItemSyncStatus.Conflict;
                    hasConflict = true;
                    conflictReason = "A local project with this name already exists (created locally or imported from another vault).";
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
                    StackHash = manifest?.StackHash,
                    LocalVersion = localVersion,
                    RemoteVersion = remoteVersion,
                    LatestChangelog = manifest?.Changelog,
                    UpdatedAt = manifest?.UpdatedAt ?? DateTimeOffset.UtcNow,
                    UpdatedBy = manifest?.UpdatedBy,
                    ReposCount = manifest?.Repos.Count ?? 0,
                    SkillsCount = skillNames.Count,
                    McpsCount = mcpNames.Count,
                    MemoriesCount = memoryNames.Count,
                    ReviewActionsCount = reviewActionNames.Count,
                    VerificationsCount = verificationNames.Count,
                    SkillNames = skillNames.ToList(),
                    McpServerNames = mcpNames.ToList(),
                    MemoryFileNames = memoryNames,
                    ReviewActionNames = reviewActionNames,
                    VerificationNames = verificationNames,
                    SyncStatus = syncStatus,
                    Repos = manifest?.Repos ?? new(),
                    HasLocalConflict = hasConflict,
                    ConflictReason = conflictReason,
                    SourceVaultId = vault?.Id,
                    SourceVaultName = vault?.Name
                });
            }
        }

        return Task.FromResult(catalog);
    }

    private VaultCatalogItem BuildLocalCatalogItem(ProjectConfig localProj, VaultSettings? vault)
    {
        var localSkillsDir = ProjectPathHelper.GetSkillsDir(_config.TendrilHome, localProj.Name);
        var skillNames = new HashSet<string>(localProj.Skills.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(localSkillsDir))
        {
            foreach (var f in Directory.GetFiles(localSkillsDir, "*.md"))
                skillNames.Add(Path.GetFileNameWithoutExtension(f));
        }

        var memoryDir = ProjectPathHelper.GetMemoryDir(_config.TendrilHome, localProj.Name);
        var memoryNames = Directory.Exists(memoryDir)
            ? Directory.GetFiles(memoryDir, "*.md").Select(Path.GetFileName).OfType<string>().ToList()
            : new List<string>();

        var reviewActionNames = localProj.ReviewActions.Select(r => r.Name).ToList();
        var verificationNames = localProj.Verifications.Select(v => v.Name).ToList();

        var repos = localProj.Repos.Select(r =>
        {
            var name = Path.GetFileName(r.Path.TrimEnd('/', '\\'));
            return new VaultRepoRef { Owner = "local", Name = name, BaseBranch = r.BaseBranch };
        }).ToList();

        return new VaultCatalogItem
        {
            Name = localProj.Name,
            Description = localProj.Context,
            Color = localProj.Color,
            StackHash = localProj.StackHash,
            LocalVersion = GetTrackedVersion(localProj.Name, vault),
            RemoteVersion = "",
            SyncStatus = VaultItemSyncStatus.LocalOnly,
            Repos = repos,
            ReposCount = localProj.Repos.Count,
            SkillsCount = skillNames.Count,
            McpsCount = localProj.McpServers.Count,
            MemoriesCount = memoryNames.Count,
            ReviewActionsCount = reviewActionNames.Count,
            VerificationsCount = verificationNames.Count,
            SkillNames = skillNames.ToList(),
            McpServerNames = localProj.McpServers.Select(m => m.Name).ToList(),
            MemoryFileNames = memoryNames,
            ReviewActionNames = reviewActionNames,
            VerificationNames = verificationNames,
            UpdatedAt = DateTimeOffset.UtcNow,
            SourceVaultId = vault?.Id,
            SourceVaultName = vault?.Name
        };
    }

    public async Task<List<GitHubAccountOption>> GetGitHubAccountsAndOrgsAsync()
    {
        var list = new List<GitHubAccountOption>();

        // 1. Current user login
        var (userOut, _) = await RunGhCliAsync("api user --jq .login");
        var userLogin = userOut?.Trim();
        if (!string.IsNullOrWhiteSpace(userLogin) && !userLogin.StartsWith("{"))
        {
            list.Add(new GitHubAccountOption(userLogin, "Personal"));
        }

        // 2. User organizations
        var (orgsOut, _) = await RunGhCliAsync("api user/orgs --jq .[].login");
        if (!string.IsNullOrWhiteSpace(orgsOut))
        {
            var orgs = orgsOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var org in orgs)
            {
                if (!string.IsNullOrWhiteSpace(org))
                {
                    list.Add(new GitHubAccountOption(org, "Organization"));
                }
            }
        }

        return list;
    }

    public async Task<List<DiscoveredVaultRepo>> DiscoverExistingVaultsAsync()
    {
        EnsureVaultsInitialized();
        var results = new List<DiscoveredVaultRepo>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var v in _config.Settings.Vaults)
        {
            if (!string.IsNullOrWhiteSpace(v.RepoUrl))
            {
                seenUrls.Add(NormalizeRepoUrl(v.RepoUrl));
            }
        }

        var accounts = await GetGitHubAccountsAndOrgsAsync();

        foreach (var acc in accounts)
        {
            // 1. Direct check for Tendril-Vault
            var (directOut, directErr) = await RunGhCliAsync($"api repos/{acc.Login}/Tendril-Vault --jq \"{{fullName: .full_name, url: .html_url, isPrivate: .private}}\"");
            if (directErr == null && !string.IsNullOrWhiteSpace(directOut))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(directOut);
                    var root = doc.RootElement;
                    var fullName = root.GetProperty("fullName").GetString() ?? $"{acc.Login}/Tendril-Vault";
                    var url = root.GetProperty("url").GetString() ?? $"https://github.com/{fullName}.git";
                    var isPriv = root.TryGetProperty("isPrivate", out var p) && p.GetBoolean();

                    var normalized = NormalizeRepoUrl(url);
                    if (!seenUrls.Contains(normalized))
                    {
                        seenUrls.Add(normalized);
                        results.Add(new DiscoveredVaultRepo(fullName, url, acc.Login, "Tendril-Vault", isPriv, acc.Type));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed parsing Tendril-Vault for account {Login}", acc.Login);
                }
            }

            // 2. Search account repos for any repo with 'vault' in the name
            var (listOut, listErr) = await RunGhCliAsync($"repo list {acc.Login} --limit 30 --json nameWithOwner,url,isPrivate,name");
            if (listErr == null && !string.IsNullOrWhiteSpace(listOut))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(listOut);
                    if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var elem in doc.RootElement.EnumerateArray())
                        {
                            var name = elem.GetProperty("name").GetString() ?? "";
                            var fullName = elem.GetProperty("nameWithOwner").GetString() ?? "";
                            var url = elem.GetProperty("url").GetString() ?? "";
                            var isPriv = elem.TryGetProperty("isPrivate", out var p) && p.GetBoolean();

                            if (name.Contains("vault", StringComparison.OrdinalIgnoreCase) &&
                                !seenUrls.Contains(NormalizeRepoUrl(url)))
                            {
                                seenUrls.Add(NormalizeRepoUrl(url));
                                results.Add(new DiscoveredVaultRepo(fullName, url, acc.Login, name, isPriv, acc.Type));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed parsing repo list for account {Login}", acc.Login);
                }
            }
        }

        return results;
    }

    public async Task<VaultResult> CreateVaultRepoAsync(string repoName, bool isPrivate = true, string? org = null)
    {
        EnsureVaultsInitialized();

        repoName = repoName.Trim();
        if (!string.IsNullOrWhiteSpace(org))
        {
            org = org.Trim();
            var spaceIdx = org.IndexOf(' ');
            if (spaceIdx > 0)
            {
                org = org[..spaceIdx].Trim();
            }
        }

        var targetRepo = !string.IsNullOrWhiteSpace(org) ? $"{org}/{repoName}" : repoName;
        var visibilityFlag = isPrivate ? "--private" : "--public";

        // Check if repo already exists on GitHub
        var (checkOut, checkErr) = await RunGhCliAsync($"api repos/{targetRepo} --jq .html_url");
        if (checkErr == null && !string.IsNullOrWhiteSpace(checkOut) && checkOut.Trim().StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return await ConnectVaultAsync(checkOut.Trim(), repoName);
        }

        // Create remote repo on GitHub
        var (createOut, createErr) = await RunGhCliAsync($"repo create {targetRepo} {visibilityFlag}");
        if (createErr != null && !createErr.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            var (retryUrlOut, retryUrlErr) = await RunGhCliAsync($"api repos/{targetRepo} --jq .html_url");
            if (retryUrlErr == null && !string.IsNullOrWhiteSpace(retryUrlOut) && retryUrlOut.Trim().StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return await ConnectVaultAsync(retryUrlOut.Trim(), repoName);
            }

            return new VaultResult(false, "Failed to create GitHub repository", createErr);
        }

        // Resolve clone / remote URL
        string repoUrl = $"https://github.com/{targetRepo}.git";
        var (newUrlOut, newUrlErr) = await RunGhCliAsync($"api repos/{targetRepo} --jq .html_url");
        if (newUrlErr == null && !string.IsNullOrWhiteSpace(newUrlOut) && newUrlOut.Trim().StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            repoUrl = newUrlOut.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(createOut) && createOut.Trim().StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            repoUrl = createOut.Trim();
        }

        var vaultId = Guid.NewGuid().ToString("N")[..8];
        var vaultDir = Path.Combine(_config.TendrilHome, "Vaults", vaultId);

        _logger.LogInformation("[Vault] Initializing local git repo in '{VaultDir}' for '{RepoUrl}'", vaultDir, repoUrl);

        if (Directory.Exists(vaultDir))
        {
            Directory.Delete(vaultDir, true);
        }

        Directory.CreateDirectory(vaultDir);
        await RunGitCommandAsync(vaultDir, "init -b main");
        await RunGitCommandAsync(vaultDir, $"remote add origin {repoUrl}");

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

        var newVault = new VaultSettings
        {
            Id = vaultId,
            Name = repoName,
            Enabled = true,
            RepoUrl = repoUrl,
            LocalPath = vaultDir,
            LastSyncedAt = DateTimeOffset.UtcNow,
            AlwaysUpToDate = false
        };

        _config.Settings.Vaults.Add(newVault);
        _config.Settings.Vault = newVault;
        _config.SaveSettings();
        VaultChanged?.Invoke();

        return new VaultResult(true, $"Created and connected vault repository '{targetRepo}'");
    }

    public async Task<VaultResult> ConnectVaultAsync(string repoUrl, string? customName = null)
    {
        if (string.IsNullOrWhiteSpace(repoUrl))
            return new VaultResult(false, "Repository URL cannot be empty", "Empty URL");

        EnsureVaultsInitialized();

        var normalizedUrl = NormalizeRepoUrl(repoUrl);
        var existingVault = _config.Settings.Vaults.FirstOrDefault(v => NormalizeRepoUrl(v.RepoUrl) == normalizedUrl);
        if (existingVault != null)
        {
            return new VaultResult(false, $"This repository is already connected as '{existingVault.Name}'.", "Duplicate vault connection");
        }

        var vaultId = Guid.NewGuid().ToString("N")[..8];
        var vaultDir = Path.Combine(_config.TendrilHome, "Vaults", vaultId);

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

        var vaultName = !string.IsNullOrWhiteSpace(customName) ? customName.Trim() : ExtractRepoName(repoUrl);
        var manifestPath = Path.Combine(vaultDir, "vault.yaml");
        if (File.Exists(manifestPath))
        {
            try
            {
                var yaml = File.ReadAllText(manifestPath);
                var manifest = YamlHelper.Deserializer.Deserialize<VaultManifest>(yaml);
                if (!string.IsNullOrWhiteSpace(manifest?.Name))
                {
                    vaultName = manifest.Name;
                }
            }
            catch { }
        }

        var newVault = new VaultSettings
        {
            Id = vaultId,
            Name = vaultName,
            Enabled = true,
            RepoUrl = repoUrl,
            LocalPath = vaultDir,
            LastSyncedAt = DateTimeOffset.UtcNow,
            AlwaysUpToDate = false
        };

        _config.Settings.Vaults.Add(newVault);
        _config.Settings.Vault = newVault;
        _config.SaveSettings();
        VaultChanged?.Invoke();

        return new VaultResult(true, $"Successfully connected to vault repository '{vaultName}'");
    }

    public Task<VaultResult> DisconnectVaultAsync(string? vaultId = null)
    {
        EnsureVaultsInitialized();
        var targetVault = GetVaultSettings(vaultId);

        if (targetVault != null)
        {
            _config.Settings.Vaults.Remove(targetVault);
            if (_config.Settings.Vault?.Id == targetVault.Id)
            {
                _config.Settings.Vault = _config.Settings.Vaults.FirstOrDefault(v => v.Enabled);
            }
            _config.SaveSettings();
        }

        VaultChanged?.Invoke();
        return Task.FromResult(new VaultResult(true, "Disconnected from vault"));
    }

    public Task<VaultResult> SetAlwaysUpToDateAsync(bool alwaysUpToDate, string? vaultId = null)
    {
        EnsureVaultsInitialized();
        var vault = GetVaultSettings(vaultId);

        if (vault != null)
        {
            vault.AlwaysUpToDate = alwaysUpToDate;
            if (_config.Settings.Vault?.Id == vault.Id)
            {
                _config.Settings.Vault.AlwaysUpToDate = alwaysUpToDate;
            }
            _config.SaveSettings();
        }

        VaultChanged?.Invoke();
        return Task.FromResult(new VaultResult(true, $"Auto-sync is now {(alwaysUpToDate ? "enabled" : "disabled")}"));
    }

    public async Task<VaultPrResult> PushAndCreatePrAsync(VaultExportRequest request, string? vaultId = null)
    {
        EnsureVaultsInitialized();
        var targetVault = GetVaultSettings(request.TargetVaultId ?? vaultId);

        if (targetVault == null)
        {
            return new VaultPrResult(false, ErrorMessage: "No vault configured to push updates.");
        }

        var vaultDir = GetVaultDirectory(targetVault);
        if (!Directory.Exists(Path.Combine(vaultDir, ".git")))
        {
            return new VaultPrResult(false, ErrorMessage: "Vault repository is not initialized locally.");
        }

        var version = !string.IsNullOrWhiteSpace(request.Version)
            ? request.Version
            : GenerateVersionTimestamp();

        var timestampId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var branchName = $"vault/update-{timestampId}";

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

            // Granular filtering
            var selectedSkills = request.SelectedSkills.TryGetValue(proj.Name, out var sSet) ? sSet : null;
            var selectedMcps = request.SelectedMcps.TryGetValue(proj.Name, out var mSet) ? mSet : null;
            var selectedActions = request.SelectedReviewActions.TryGetValue(proj.Name, out var aSet) ? aSet : null;
            var selectedVerifs = request.SelectedVerifications.TryGetValue(proj.Name, out var vSet) ? vSet : null;
            var selectedMems = request.SelectedMemories.TryGetValue(proj.Name, out var memSet) ? memSet : null;
            var syncPermissions = request.SyncPermissions.TryGetValue(proj.Name, out var pSync) ? pSync : true;

            var exportedSkills = selectedSkills != null
                ? proj.Skills.Where(s => selectedSkills.Contains(s.Name)).ToList()
                : proj.Skills;

            var exportedMcps = selectedMcps != null
                ? proj.McpServers.Where(m => selectedMcps.Contains(m.Name)).ToList()
                : proj.McpServers;

            var exportedActions = selectedActions != null
                ? proj.ReviewActions.Where(a => selectedActions.Contains(a.Name)).ToList()
                : proj.ReviewActions;

            var exportedVerifications = selectedVerifs != null
                ? proj.Verifications.Where(v => selectedVerifs.Contains(v.Name)).ToList()
                : proj.Verifications;

            // Extract repo remote URLs
            var repoRefs = new List<VaultRepoRef>();
            foreach (var r in proj.Repos)
            {
                var repoPath = r.Path;
                var remoteUrl = "";
                var owner = "default";
                var rName = Path.GetFileName(repoPath.TrimEnd('/', '\\'));

                if (Directory.Exists(repoPath))
                {
                    var (remoteOut, _) = await RunGitCommandAsync(repoPath, "config --get remote.origin.url");
                    if (!string.IsNullOrWhiteSpace(remoteOut))
                    {
                        remoteUrl = remoteOut.Trim();
                        var match = Regex.Match(remoteUrl, @"[:/]([^/]+)/([^/]+?)(?:\.git)?$");
                        if (match.Success)
                        {
                            owner = match.Groups[1].Value;
                            rName = match.Groups[2].Value;
                        }
                    }
                }

                repoRefs.Add(new VaultRepoRef
                {
                    Owner = owner,
                    Name = rName,
                    BaseBranch = r.BaseBranch,
                    RemoteUrl = !string.IsNullOrEmpty(remoteUrl) ? remoteUrl : null
                });
            }

            // Include verification definitions
            var usedVerifNames = new HashSet<string>(exportedVerifications.Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
            var verifDefinitions = _config.Settings.Verifications
                .Where(v => usedVerifNames.Contains(v.Name))
                .ToList();

            // 1. Export project.yaml
            var projectManifest = new VaultProjectManifest
            {
                Name = proj.Name,
                Version = version,
                UpdatedAt = DateTimeOffset.UtcNow,
                Changelog = request.Changelog,
                Color = proj.Color,
                Context = proj.Context,
                StackHash = proj.StackHash,
                Meta = proj.Meta ?? new(),
                Repos = repoRefs,
                Verifications = exportedVerifications,
                VerificationDefinitions = verifDefinitions,
                ReviewActions = exportedActions,
                Hooks = proj.Hooks ?? new(),
                BuildDependencies = proj.BuildDependencies ?? new(),
                McpServers = VaultSecretSanitizer.SanitizeMcpServers(exportedMcps),
                Skills = exportedSkills,
                SecurityPreset = proj.SecurityPreset,
                OutsideFileAccessPolicy = proj.OutsideFileAccessPolicy,
                TerminalAutoExecution = proj.TerminalAutoExecution,
                SandboxMode = proj.SandboxMode,
                AutoImplementPlans = proj.AutoImplementPlans
            };

            var projYaml = YamlHelper.SerializerCompact.Serialize(projectManifest);
            File.WriteAllText(Path.Combine(projDir, "project.yaml"), projYaml);

            // 2. Export permissions.yaml if enabled
            if (syncPermissions)
            {
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
            }

            // 3. Export disk skills
            var localSkillsDir = ProjectPathHelper.GetSkillsDir(_config.TendrilHome, proj.Name);
            var vaultSkillsDir = Path.Combine(projDir, "skills");
            if (Directory.Exists(localSkillsDir))
            {
                Directory.CreateDirectory(vaultSkillsDir);
                foreach (var file in Directory.GetFiles(localSkillsDir, "*.md"))
                {
                    var skillName = Path.GetFileNameWithoutExtension(file);
                    if (selectedSkills == null || selectedSkills.Contains(skillName))
                    {
                        File.Copy(file, Path.Combine(vaultSkillsDir, Path.GetFileName(file)), true);
                    }
                }
            }

            // 4. Export project memories
            var localMemoryDir = ProjectPathHelper.GetMemoryDir(_config.TendrilHome, proj.Name);
            var vaultMemoryDir = Path.Combine(projDir, "memory");
            if (Directory.Exists(localMemoryDir))
            {
                Directory.CreateDirectory(vaultMemoryDir);
                foreach (var file in Directory.GetFiles(localMemoryDir, "*.md"))
                {
                    var fileName = Path.GetFileName(file);
                    if (selectedMems == null || selectedMems.Contains(fileName))
                    {
                        File.Copy(file, Path.Combine(vaultMemoryDir, fileName), true);
                    }
                }
            }

            // Update local tracking
            targetVault.TrackedProjects[proj.Name] = new ProjectVaultTracking
            {
                InstalledVersion = version,
                InstalledAt = DateTimeOffset.UtcNow,
                VaultId = targetVault.Id,
                VaultRepoUrl = targetVault.RepoUrl,
                LocalRepoPaths = proj.Repos.ToDictionary(r => Path.GetFileName(r.Path.TrimEnd('/', '\\')), r => r.Path)
            };

            exportedProjects.Add(proj.Name);
        }

        var rootManifest = new VaultManifest
        {
            Name = targetVault.Name,
            Version = version,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        File.WriteAllText(Path.Combine(vaultDir, "vault.yaml"), YamlHelper.SerializerCompact.Serialize(rootManifest));

        await RunGitCommandAsync(vaultDir, "add -A");
        var commitMsg = !string.IsNullOrWhiteSpace(request.Changelog)
            ? $"feat(vault): update {string.Join(", ", exportedProjects)} (v{version})\n\n{request.Changelog}"
            : $"feat(vault): update {string.Join(", ", exportedProjects)} (v{version})";

        await RunGitCommandAsync(vaultDir, $"commit -m \"{commitMsg.Replace("\"", "\\\"")}\"");
        await RunGitCommandAsync(vaultDir, $"push -u origin {branchName}");

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

        targetVault.LastSyncedAt = DateTimeOffset.UtcNow;
        _config.SaveSettings();
        VaultChanged?.Invoke();

        return new VaultPrResult(true, PrUrl: prUrl, BranchName: branchName);
    }

    public Task<VaultResult> ImportProjectAsync(string projectName, Dictionary<string, string> localRepoMappings, string? vaultId = null)
    {
        return ImportProjectAsync(new VaultImportRequest
        {
            ProjectName = projectName,
            LocalRepoMappings = localRepoMappings,
            SourceVaultId = vaultId
        }, vaultId);
    }

    public async Task<VaultResult> ImportProjectAsync(VaultImportRequest request, string? vaultId = null)
    {
        EnsureVaultsInitialized();
        var vault = GetVaultSettings(request.SourceVaultId ?? vaultId);

        if (vault == null)
        {
            return new VaultResult(false, "No vault configured for import.", "Vault not found");
        }

        var projectName = request.ProjectName;
        var vaultDir = GetVaultDirectory(vault);
        var projDir = Path.Combine(vaultDir, "projects", projectName);

        if (!Directory.Exists(projDir))
        {
            return new VaultResult(false, $"Project '{projectName}' was not found in vault '{vault.Name}'.", "Directory not found");
        }

        var projManifestPath = Path.Combine(projDir, "project.yaml");
        if (!File.Exists(projManifestPath))
        {
            return new VaultResult(false, $"Project manifest for '{projectName}' missing.", "project.yaml missing");
        }

        var yaml = await File.ReadAllTextAsync(projManifestPath);
        var manifest = YamlHelper.Deserializer.Deserialize<VaultProjectManifest>(yaml);

        var finalLocalProjectName = !string.IsNullOrWhiteSpace(request.TargetLocalProjectName)
            ? request.TargetLocalProjectName.Trim()
            : manifest.Name;

        // 1. Merge verification definitions if present
        if (manifest.VerificationDefinitions != null)
        {
            foreach (var def in manifest.VerificationDefinitions)
            {
                if (!_config.Settings.Verifications.Any(v => v.Name.Equals(def.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    _config.Settings.Verifications.Add(def);
                }
            }
        }

        // 2. Resolve or clone repositories
        var repos = new List<RepoRef>();
        foreach (var r in manifest.Repos)
        {
            var key = $"{r.Owner}/{r.Name}";
            if (!request.LocalRepoMappings.TryGetValue(key, out var path) &&
                !request.LocalRepoMappings.TryGetValue(r.Name, out path))
            {
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "git", r.Name);
            }

            // Auto-clone if folder doesn't exist locally and remote URL is available
            if (!Directory.Exists(path) && !string.IsNullOrWhiteSpace(r.RemoteUrl))
            {
                try
                {
                    var parentDir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(parentDir)) Directory.CreateDirectory(parentDir);

                    _logger.LogInformation("Cloning repository {RemoteUrl} into {Path}...", r.RemoteUrl, path);
                    var (cloneOut, cloneErr) = await RunGitCommandAsync(parentDir ?? Path.GetTempPath(), $"clone {r.RemoteUrl} {path}");
                    if (cloneErr != null && !Directory.Exists(Path.Combine(path, ".git")))
                    {
                        await RunGhCliAsync($"repo clone {r.Owner}/{r.Name} {path}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to auto-clone {RemoteUrl}", r.RemoteUrl);
                }
            }

            repos.Add(new RepoRef { Path = path, BaseBranch = r.BaseBranch });
        }

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

        var filteredSkills = request.SelectedSkills != null
            ? manifest.Skills.Where(s => request.SelectedSkills.Contains(s.Name)).ToList()
            : manifest.Skills;

        var filteredMcps = request.SelectedMcps != null
            ? manifest.McpServers.Where(m => request.SelectedMcps.Contains(m.Name)).ToList()
            : manifest.McpServers;

        var filteredActions = request.SelectedReviewActions != null
            ? manifest.ReviewActions.Where(a => request.SelectedReviewActions.Contains(a.Name)).ToList()
            : manifest.ReviewActions;

        var filteredVerifications = request.SelectedVerifications != null
            ? manifest.Verifications.Where(v => request.SelectedVerifications.Contains(v.Name)).ToList()
            : manifest.Verifications;

        var projectConfig = new ProjectConfig
        {
            Name = finalLocalProjectName,
            Color = manifest.Color,
            Context = manifest.Context,
            StackHash = manifest.StackHash,
            Meta = manifest.Meta ?? new(),
            Repos = repos,
            Verifications = filteredVerifications,
            ReviewActions = filteredActions,
            Hooks = manifest.Hooks ?? new(),
            BuildDependencies = manifest.BuildDependencies ?? new(),
            McpServers = filteredMcps,
            Skills = filteredSkills,
            SecurityPreset = manifest.SecurityPreset,
            OutsideFileAccessPolicy = request.ImportPermissions ? (permManifest?.OutsideFileAccessPolicy ?? manifest.OutsideFileAccessPolicy) : manifest.OutsideFileAccessPolicy,
            TerminalAutoExecution = request.ImportPermissions ? (permManifest?.TerminalAutoExecution ?? manifest.TerminalAutoExecution) : manifest.TerminalAutoExecution,
            SandboxMode = request.ImportPermissions ? (permManifest?.SandboxMode ?? manifest.SandboxMode) : manifest.SandboxMode,
            AutoImplementPlans = manifest.AutoImplementPlans,
            FilePermissions = request.ImportPermissions ? (permManifest?.FilePermissions ?? new()) : new(),
            NetworkAccessRules = request.ImportPermissions ? (permManifest?.NetworkAccessRules ?? new()) : new(),
            AllowedTerminalCommands = request.ImportPermissions ? (permManifest?.AllowedTerminalCommands ?? new()) : new()
        };

        var existingIdx = _config.Settings.Projects.FindIndex(p => p.Name.Equals(finalLocalProjectName, StringComparison.OrdinalIgnoreCase));
        if (existingIdx >= 0)
        {
            _config.Settings.Projects[existingIdx] = projectConfig;
        }
        else
        {
            _config.Settings.Projects.Add(projectConfig);
        }

        ProjectPathHelper.EnsureProjectDirectories(_config.TendrilHome, finalLocalProjectName);

        // Copy skills
        var vaultSkillsDir = Path.Combine(projDir, "skills");
        var localSkillsDir = ProjectPathHelper.GetSkillsDir(_config.TendrilHome, finalLocalProjectName);

        if (Directory.Exists(vaultSkillsDir))
        {
            foreach (var file in Directory.GetFiles(vaultSkillsDir, "*.md"))
            {
                var skillName = Path.GetFileNameWithoutExtension(file);
                if (request.SelectedSkills == null || request.SelectedSkills.Contains(skillName))
                {
                    File.Copy(file, Path.Combine(localSkillsDir, Path.GetFileName(file)), true);
                }
            }
        }

        // Copy memories
        var vaultMemoryDir = Path.Combine(projDir, "memory");
        var localMemoryDir = ProjectPathHelper.GetMemoryDir(_config.TendrilHome, finalLocalProjectName);

        if (Directory.Exists(vaultMemoryDir))
        {
            foreach (var file in Directory.GetFiles(vaultMemoryDir, "*.md"))
            {
                var memName = Path.GetFileName(file);
                if (request.SelectedMemories == null || request.SelectedMemories.Contains(memName))
                {
                    File.Copy(file, Path.Combine(localMemoryDir, memName), true);
                }
            }
        }

        vault.TrackedProjects[finalLocalProjectName] = new ProjectVaultTracking
        {
            InstalledVersion = manifest.Version,
            InstalledAt = DateTimeOffset.UtcNow,
            VaultId = vault.Id,
            VaultRepoUrl = vault.RepoUrl,
            LocalRepoPaths = request.LocalRepoMappings
        };

        _config.SaveSettings();
        VaultChanged?.Invoke();

        return new VaultResult(true, $"Successfully imported project '{finalLocalProjectName}' (v{manifest.Version})");
    }

    public async Task<VaultPrResult> DeleteProjectFromVaultAsync(string projectName, string? vaultId = null)
    {
        EnsureVaultsInitialized();
        var vault = GetVaultSettings(vaultId);

        if (vault == null)
        {
            return new VaultPrResult(false, ErrorMessage: "No vault configured for deletion.");
        }

        var vaultDir = GetVaultDirectory(vault);
        if (!Directory.Exists(Path.Combine(vaultDir, ".git")))
        {
            return new VaultPrResult(false, ErrorMessage: "Vault repository is not initialized locally.");
        }

        var projDir = Path.Combine(vaultDir, "projects", projectName);
        if (!Directory.Exists(projDir))
        {
            return new VaultPrResult(false, ErrorMessage: $"Project '{projectName}' was not found in vault '{vault.Name}'.");
        }

        var version = GenerateVersionTimestamp();
        var timestampId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var branchName = $"vault/delete-{projectName.ToLowerInvariant()}-{timestampId}";

        try
        {
            await RunGitCommandAsync(vaultDir, "checkout main");
            await RunGitCommandAsync(vaultDir, "pull origin main");
            await RunGitCommandAsync(vaultDir, $"checkout -B {branchName}");

            Directory.Delete(projDir, true);

            var rootManifest = new VaultManifest
            {
                Name = vault.Name,
                Version = version,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            File.WriteAllText(Path.Combine(vaultDir, "vault.yaml"), YamlHelper.SerializerCompact.Serialize(rootManifest));

            await RunGitCommandAsync(vaultDir, "add -A");
            await RunGitCommandAsync(vaultDir, $"commit -m \"chore(vault): delete {projectName} from vault (v{version})\"");
            await RunGitCommandAsync(vaultDir, $"push -u origin {branchName}");

            var prTitle = $"Delete {projectName} from vault (v{version})";
            var prBody = $"### Vault Project Deletion\n\nThis PR removes the **{projectName}** project and its assets from the vault.";

            var ghPrCmd = $"pr create --title \"{prTitle.Replace("\"", "\\\"")}\" --body \"{prBody.Replace("\"", "\\\"")}\" --head \"{branchName}\"";
            var (prOut, _) = await RunGhCliAsync(ghPrCmd, vaultDir);
            var prUrl = prOut?.Trim();

            vault.TrackedProjects.Remove(projectName);
            if (_config.Settings.Vault != null && _config.Settings.Vault.Id == vault.Id)
            {
                _config.Settings.Vault.TrackedProjects.Remove(projectName);
            }

            vault.LastSyncedAt = DateTimeOffset.UtcNow;
            _config.SaveSettings();
            VaultChanged?.Invoke();

            return new VaultPrResult(true, PrUrl: prUrl, BranchName: branchName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create PR for deleting project {Project}", projectName);
            return new VaultPrResult(false, ErrorMessage: ex.Message);
        }
    }

    public async Task<VaultSyncResult> PullLatestAsync(string? vaultId = null)
    {
        EnsureVaultsInitialized();
        var vaultsToSync = !string.IsNullOrEmpty(vaultId)
            ? _config.Settings.Vaults.Where(v => v.Id.Equals(vaultId, StringComparison.OrdinalIgnoreCase) || v.RepoUrl.Equals(vaultId, StringComparison.OrdinalIgnoreCase)).ToList()
            : _config.Settings.Vaults.Where(v => v.Enabled).ToList();

        if (vaultsToSync.Count == 0 && _config.Settings.Vault != null && _config.Settings.Vault.Enabled)
        {
            vaultsToSync.Add(_config.Settings.Vault);
        }

        if (vaultsToSync.Count == 0)
        {
            return new VaultSyncResult(false, Message: "No vaults are configured.");
        }

        int totalUpdated = 0;
        string? firstError = null;

        foreach (var vault in vaultsToSync)
        {
            var vaultDir = GetVaultDirectory(vault);
            if (!Directory.Exists(Path.Combine(vaultDir, ".git")))
            {
                continue;
            }

            var (pullOut, pullErr) = await RunGitCommandAsync(vaultDir, "pull --rebase origin main");
            if (pullErr != null && pullErr.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                firstError ??= pullErr;
                continue;
            }

            vault.LastSyncedAt = DateTimeOffset.UtcNow;

            if (vault.AlwaysUpToDate)
            {
                var catalog = await GetCatalogAsync(vault.Id);
                foreach (var item in catalog.Projects)
                {
                    if (item.SyncStatus == VaultItemSyncStatus.UpdateAvailable)
                    {
                        var tracking = vault.TrackedProjects.TryGetValue(item.Name, out var t) ? t : null;
                        var mappings = tracking?.LocalRepoPaths ?? new();
                        await ImportProjectAsync(new VaultImportRequest
                        {
                            ProjectName = item.Name,
                            LocalRepoMappings = mappings,
                            SourceVaultId = vault.Id
                        }, vault.Id);
                        totalUpdated++;
                    }
                }
            }
        }

        _config.SaveSettings();
        VaultChanged?.Invoke();

        if (firstError != null && totalUpdated == 0)
        {
            return new VaultSyncResult(false, Message: "Failed to pull latest vault changes", ErrorMessage: firstError);
        }

        return new VaultSyncResult(true, UpdatedProjectsCount: totalUpdated, Message: "Vaults synchronized successfully");
    }

    private string? GetTrackedVersion(string projectName, VaultSettings? vault)
    {
        if (vault != null && vault.TrackedProjects.TryGetValue(projectName, out var tracking))
        {
            return tracking.InstalledVersion;
        }

        if (_config.Settings.Vault != null && _config.Settings.Vault.TrackedProjects.TryGetValue(projectName, out var mainTracking))
        {
            return mainTracking.InstalledVersion;
        }

        return null;
    }

    private static List<string> TokenizeArguments(string arguments)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(arguments)) return tokens;

        var sb = new StringBuilder();
        bool inQuotes = false;
        char quoteChar = '\0';

        for (int i = 0; i < arguments.Length; i++)
        {
            char c = arguments[i];

            if (c == '\\' && i + 1 < arguments.Length && (arguments[i + 1] == '"' || arguments[i + 1] == '\''))
            {
                sb.Append(arguments[++i]);
            }
            else if (!inQuotes && (c == '"' || c == '\''))
            {
                inQuotes = true;
                quoteChar = c;
            }
            else if (inQuotes && c == quoteChar)
            {
                inQuotes = false;
                quoteChar = '\0';
            }
            else if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (sb.Length > 0)
                {
                    tokens.Add(sb.ToString());
                    sb.Clear();
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 0)
        {
            tokens.Add(sb.ToString());
        }

        return tokens;
    }

    private static async Task<(string? output, string? error)> RunGitCommandAsync(string workingDir, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (var arg in TokenizeArguments(arguments))
            {
                psi.ArgumentList.Add(arg);
            }

            using var proc = Process.Start(psi);
            if (proc == null) return (null, "Failed to start git process");

            var stdout = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            if (proc.ExitCode != 0)
            {
                var err = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : stdout?.Trim();
                return (null, err);
            }

            return (stdout, null);
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
            var psi = new ProcessStartInfo("gh")
            {
                WorkingDirectory = workingDir ?? Path.GetTempPath(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (var arg in TokenizeArguments(arguments))
            {
                psi.ArgumentList.Add(arg);
            }

            using var proc = Process.Start(psi);
            if (proc == null) return (null, "GitHub CLI (gh) is not available.");

            var stdout = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            if (proc.ExitCode != 0)
            {
                var err = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : stdout?.Trim();
                return (null, err);
            }

            return (stdout, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }
}
