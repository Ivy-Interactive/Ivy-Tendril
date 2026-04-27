using Ivy.Tendril.Helpers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;

namespace Ivy.Tendril.Services;

public record RepoConfig
{
    public string Owner { get; set; } = "";
    public string Name { get; set; } = "";
    public string FullName => $"{Owner}/{Name}";
    public string DisplayName => Name;
}

public record RepoRef
{
    public string Path { get; set; } = "";
    public string PrRule { get; set; } = "default";
    public string? BaseBranch { get; set; }
    public string SyncStrategy { get; set; } = "fetch";
}

public record ProjectConfig
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public Dictionary<string, object> Meta { get; set; } = new();
    public List<RepoRef> Repos { get; set; } = new();
    public List<ProjectVerificationRef> Verifications { get; set; } = new();
    public string Context { get; set; } = "";
    public List<ReviewActionConfig> ReviewActions { get; set; } = new();
    public List<PromptwareHookConfig> Hooks { get; set; } = new();
    public List<string> BuildDependencies { get; set; } = new();
    public List<string> RepoPaths => Repos.Select(r => r.Path).ToList();

    public string? GetMeta(string key)
    {
        return Meta.TryGetValue(key, out var v) ? v?.ToString() : null;
    }

    public RepoRef? GetRepoRef(string path)
    {
        return Repos.FirstOrDefault(r => r.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
    }
}

public record LevelConfig
{
    public string Name { get; set; } = "";
    public string Badge { get; set; } = "Outline";
}

public record VerificationConfig
{
    public string Name { get; set; } = "";
    public string Prompt { get; set; } = "";
}

public record ProjectVerificationRef
{
    public string Name { get; set; } = "";
    public bool Required { get; set; }
}

public record ReviewActionConfig
{
    public string Name { get; set; } = "";
    public string Condition { get; set; } = "";
    public string Action { get; set; } = "";
}

public record PromptwareHookConfig
{
    public string Name { get; set; } = "";
    public string When { get; set; } = "before";
    public List<string> Promptwares { get; set; } = new();
    public string Condition { get; set; } = "";
    public string Action { get; set; } = "";
}

public record EditorConfig
{
    public string Command { get; set; } = "code";
    public string Label { get; set; } = "VS Code";
    [YamlIgnore] public bool IsAvailable { get; set; } = true;
}

public record PromptwareConfig
{
    public string Profile { get; set; } = "";
    public List<string> AllowedTools { get; set; } = new();
}

public record AgentProfileConfig
{
    public string Name { get; set; } = "";
    public string Model { get; set; } = "";
    public string Effort { get; set; } = "";
    public string Arguments { get; set; } = "";
}

public record AgentConfig
{
    public string Name { get; set; } = "";
    public string Arguments { get; set; } = "";
    public List<AgentProfileConfig> Profiles { get; set; } = new();
}

public record LlmConfig
{
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4o-mini";
}

public record AuthConfig
{
    public string? Username { get; set; }
    public string Password { get; set; } = "";
    public string HashSecret { get; set; } = "";
    public Auth.LoginRateLimitConfig? RateLimit { get; set; }
}

public record ApiSettings
{
    public string? ApiKey { get; set; }
}

public class TendrilSettings
{
    public string CodingAgent { get; set; } = "claude";
    public int JobTimeout { get; set; } = 30;
    public int StaleOutputTimeout { get; set; } = 10;
    public int GitTimeout { get; set; } = 10;
    public int MaxConcurrentJobs { get; set; } = 5;
    public List<ProjectConfig> Projects { get; set; } = new();
    public List<VerificationConfig> Verifications { get; set; } = new();
    public string PlanTemplate { get; set; } = "";
    public EditorConfig Editor { get; set; } = new();
    public LlmConfig? Llm { get; set; }
    public AuthConfig? Auth { get; set; }
    public ApiSettings? Api { get; set; }
    public Dictionary<string, PromptwareConfig> Promptwares { get; set; } = new();
    public List<AgentConfig> CodingAgents { get; set; } = new();
    public bool Telemetry { get; set; } = true;

    public List<LevelConfig> Levels { get; set; } = new()
    {
        new LevelConfig { Name = "Bug", Badge = "Destructive" },
        new LevelConfig { Name = "Critical", Badge = "Warning" },
        new LevelConfig { Name = "NiceToHave", Badge = "Outline" },
        new LevelConfig { Name = "Epic", Badge = "Info" }
    };
}

public record ConfigParseError(string Message, string FilePath, Exception? InnerException);

public class ConfigService : IConfigService, IDisposable
{
    private readonly bool _explicitHome;
    private readonly ILogger<ConfigService> _logger;
    private readonly HashSet<string> _tempFiles = new();
    private readonly object _tempFilesLock = new();
    private string[]? _levelNamesCache;
    private string? _pendingCodingAgent;
    private ProjectConfig? _pendingProject;
    private string? _pendingTendrilHome;
    private List<VerificationConfig>? _pendingVerificationDefinitions;
    private bool _disposed;

    internal ConfigService(TendrilSettings settings, string? tendrilHome = null, ILogger<ConfigService>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigService>.Instance;
        Settings = settings;
        TendrilHome = tendrilHome ?? Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        _explicitHome = tendrilHome != null;
        ConfigPath = !string.IsNullOrEmpty(TendrilHome)
            ? Path.Combine(TendrilHome, "config.yaml")
            : Path.Combine(System.AppContext.BaseDirectory, "config.yaml");
    }

    public ConfigService(ILogger<ConfigService>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigService>.Instance;

        var tendrilHomeResult = InitializeTendrilHome();
        if (!tendrilHomeResult.Success)
        {
            NeedsOnboarding = true;
            Settings = new TendrilSettings();
            ConfigPath = Path.Combine(System.AppContext.BaseDirectory, "config.yaml");
            TendrilHome = "";
            return;
        }

        TendrilHome = tendrilHomeResult.Path;
        ConfigPath = Path.Combine(TendrilHome, "config.yaml");

        var loadResult = TryLoadConfig();
        if (!loadResult.Success)
        {
            NeedsOnboarding = true;
            Settings = new TendrilSettings();
            return;
        }

        Settings = loadResult.Config;
        FinalizeConfiguration();
    }

    private (bool Success, string Path) InitializeTendrilHome()
    {
        var tendrilHomeEnv = Environment.GetEnvironmentVariable("TENDRIL_HOME")?.Trim();

        // Remove quotes if present
        if (!string.IsNullOrEmpty(tendrilHomeEnv) &&
            tendrilHomeEnv.StartsWith("\"") &&
            tendrilHomeEnv.EndsWith("\""))
        {
            tendrilHomeEnv = tendrilHomeEnv[1..^1];
        }

        return string.IsNullOrEmpty(tendrilHomeEnv)
            ? (false, "")
            : (true, tendrilHomeEnv);
    }

    private (bool Success, TendrilSettings Config) TryLoadConfig()
    {
        if (!File.Exists(ConfigPath))
            return (false, new TendrilSettings());

        try
        {
            var yaml = FileHelper.ReadAllText(ConfigPath);
            yaml = QuoteUnquotedVariablePatterns(yaml);
            var settings = YamlHelper.Deserializer.Deserialize<TendrilSettings>(yaml)
                           ?? new TendrilSettings();

            MigrateProjectColors();
            CreateConfigBackup();

            return (true, settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Tendril config {ConfigPath}", ConfigPath);
            ParseError = new ConfigParseError(ex.Message, ConfigPath, ex);
            BackupBrokenConfig();

            if (TryAutoHeal())
            {
                ParseError = null;
                return (true, Settings);
            }

            return (false, new TendrilSettings());
        }
    }

    private string QuoteUnquotedVariablePatterns(string yaml)
    {
        yaml = Regex.Replace(yaml, @"(?m)(?<=:\s+)(%\w+%.*)$", "'$1'");
        yaml = Regex.Replace(yaml, @"(?m)^(\s*-\s+)(%\w+%.*)$", "$1'$2'");
        return yaml;
    }

    private void FinalizeConfiguration()
    {
        ValidateSettings();
        VariableExpansion.InitializeUserSecrets(TendrilHome, _logger);
        ExpandSettingsVariables();
        ExpandRepoPaths();
        ValidateRepoPathsAreNotWorktrees();
        CreateRequiredDirectories();
    }

    private void CreateRequiredDirectories()
    {
        Directory.CreateDirectory(TendrilHome);
        Directory.CreateDirectory(Path.Combine(TendrilHome, "Inbox"));
        Directory.CreateDirectory(PlanFolder);
        Directory.CreateDirectory(Path.Combine(TendrilHome, "Trash"));
        Directory.CreateDirectory(Path.Combine(TendrilHome, "Promptwares"));
        Directory.CreateDirectory(Path.Combine(TendrilHome, "Hooks"));
    }

    private void ValidateSettings()
    {
        // JobTimeout: 1-480 minutes (8 hours max)
        if (Settings.JobTimeout < 1 || Settings.JobTimeout > 480)
        {
            _logger.LogWarning("JobTimeout {Value} is out of bounds (1-480 minutes). Using default 30.",
                Settings.JobTimeout);
            Settings.JobTimeout = 30;
        }

        // StaleOutputTimeout: 1-60 minutes
        if (Settings.StaleOutputTimeout < 1 || Settings.StaleOutputTimeout > 60)
        {
            _logger.LogWarning("StaleOutputTimeout {Value} is out of bounds (1-60 minutes). Using default 10.",
                Settings.StaleOutputTimeout);
            Settings.StaleOutputTimeout = 10;
        }

        // GitTimeout: 1-30 minutes
        if (Settings.GitTimeout < 1 || Settings.GitTimeout > 30)
        {
            _logger.LogWarning("GitTimeout {Value} is out of bounds (1-30 minutes). Using default 10.",
                Settings.GitTimeout);
            Settings.GitTimeout = 10;
        }

        // MaxConcurrentJobs: 1-100
        if (Settings.MaxConcurrentJobs < 1 || Settings.MaxConcurrentJobs > 100)
        {
            _logger.LogWarning("MaxConcurrentJobs {Value} is out of bounds (1-100). Using default 5.",
                Settings.MaxConcurrentJobs);
            Settings.MaxConcurrentJobs = 5;
        }
    }

    public TendrilSettings Settings { get; private set; }

    public string TendrilHome { get; private set; }

    public string ConfigPath { get; private set; }

    public string PlanFolder =>
        !_explicitHome && Environment.GetEnvironmentVariable("TENDRIL_PLANS")?.Trim() is { Length: > 0 } plans
            ? plans
            : string.IsNullOrEmpty(TendrilHome) ? "" : Path.Combine(TendrilHome, "Plans");

    public List<ProjectConfig> Projects => Settings.Projects;

    // Levels are returned in the order defined in config.yaml (not sorted).
    // Users can reorder levels in the Settings UI, and the order is preserved.
    public List<LevelConfig> Levels => Settings.Levels;

    public string[] LevelNames
    {
        get
        {
            if (_levelNamesCache == null) _levelNamesCache = Settings.Levels.Select(l => l.Name).ToArray();
            return _levelNamesCache;
        }
    }

    public EditorConfig Editor => Settings.Editor;

    public ProjectConfig? GetProject(string name)
    {
        return Settings.Projects.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public BadgeVariant GetBadgeVariant(string level)
    {
        return Enum.TryParse<BadgeVariant>(Settings.Levels.FirstOrDefault(l => l.Name == level)?.Badge ?? "Outline",
            out var v)
            ? v
            : BadgeVariant.Outline;
    }

    public Colors? GetProjectColor(string projectName)
    {
        var colorStr = GetProject(projectName)?.Color;
        return !string.IsNullOrEmpty(colorStr) && Enum.TryParse<Colors>(colorStr, out var c) ? c : null;
    }

    public event EventHandler? SettingsReloaded;

    public void SaveSettings()
    {
        _levelNamesCache = null;
        var yaml = YamlHelper.SerializerCompact.Serialize(Settings);

        // Validate the YAML can be deserialized back before writing
        try
        {
            YamlHelper.Deserializer.Deserialize<TendrilSettings>(yaml);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Generated YAML failed validation and was not saved: {ex.Message}", ex);
        }

        FileHelper.WriteAllText(ConfigPath, yaml);
        CreateConfigBackup();
        ReloadSettings();
    }

    public void ReloadSettings()
    {
        if (!File.Exists(ConfigPath))
            return;

        try
        {
            var yaml = FileHelper.ReadAllText(ConfigPath);
            yaml = Regex.Replace(yaml, @"(?m)(?<=:\s+)(%\w+%.*)$", "'$1'");
            yaml = Regex.Replace(yaml, @"(?m)^(\s*-\s+)(%\w+%.*)$", "$1'$2'");
            Settings = YamlHelper.Deserializer.Deserialize<TendrilSettings>(yaml) ?? new TendrilSettings();

            ValidateSettings();
            MigrateProjectColors();
            _levelNamesCache = null;
            VariableExpansion.InitializeUserSecrets(TendrilHome, _logger);
            ExpandSettingsVariables();

            SettingsReloaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reload settings");
        }
    }

    // Onboarding support
    public bool NeedsOnboarding { get; private set; }

    public ConfigParseError? ParseError { get; private set; }

    public void SetPendingTendrilHome(string path)
    {
        _pendingTendrilHome = path;
    }

    public string? GetPendingTendrilHome()
    {
        return _pendingTendrilHome;
    }

    public void SetPendingCodingAgent(string name)
    {
        _pendingCodingAgent = name;
    }

    public string? GetPendingCodingAgent()
    {
        return _pendingCodingAgent;
    }

    public void SetPendingProject(ProjectConfig project)
    {
        _pendingProject = project;
    }

    public ProjectConfig? GetPendingProject()
    {
        return _pendingProject;
    }

    public void SetPendingVerificationDefinitions(List<VerificationConfig> definitions)
    {
        _pendingVerificationDefinitions = definitions;
    }

    public List<VerificationConfig>? GetPendingVerificationDefinitions()
    {
        return _pendingVerificationDefinitions;
    }

    public void OpenInEditor(string path)
    {
        var processedPath = PreprocessForEditing(path);
        PlatformHelper.OpenInEditor(Editor.Command, processedPath);
    }

    public string PreprocessForEditing(string path)
    {
        if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return path;

        var content = File.ReadAllText(path);
        var repoPaths = Projects
            .SelectMany(p => p.Repos.Select(r => r.Path))
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();
        var polisher = new MarkdownLinkPolisher();
        var polished = polisher.PolishLinks(content, repoPaths, PlanFolder);

        if (content == polished)
            return path;

        var tempPath = Path.Combine(Path.GetTempPath(), $"tendril-edit-{Guid.NewGuid()}.md");
        File.WriteAllText(tempPath, polished);

        lock (_tempFilesLock)
        {
            _tempFiles.Add(tempPath);
        }

        return tempPath;
    }

    public void CompleteOnboarding(string tendrilHome)
    {
        SetTendrilHome(tendrilHome);

        if (_pendingCodingAgent != null)
            Settings.CodingAgent = _pendingCodingAgent;

        SaveSettings();
        NeedsOnboarding = false;
    }

    internal void ValidateRepoPathsAreNotWorktrees()
    {
        if (Settings?.Projects == null) return;

        var allRepos = Settings.Projects
            .Where(p => p.Repos != null)
            .SelectMany(p => p.Repos.Select(r => new { p.Name, Repo = r }))
            .Where(x => !string.IsNullOrEmpty(x.Repo.Path) && Directory.Exists(x.Repo.Path));

        foreach (var item in allRepos)
        {
            if (WorktreeValidationHelper.IsWorktree(item.Repo.Path))
            {
                _logger.LogError(
                    "CRITICAL: Project {ProjectName} repo path is a worktree, not a main repository: {RepoPath}. " +
                    "This will cause nested worktrees during plan execution. " +
                    "Update config.yaml to point to the main repo, not a worktree.",
                    item.Name, item.Repo.Path);
            }
        }
    }

    internal static bool IsCommandAvailable(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which",
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(3000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool TryAutoHeal()
    {
        // Strategy 1: Restore from last known good backup
        var backupPath = ConfigPath + ".backup";
        if (File.Exists(backupPath))
        {
            try
            {
                var backupYaml = FileHelper.ReadAllText(backupPath);
                var restored = YamlHelper.Deserializer.Deserialize<TendrilSettings>(backupYaml);
                if (restored != null)
                {
                    FileHelper.WriteAllText(ConfigPath, backupYaml);
                    Settings = restored;
                    MigrateProjectColors();
                    VariableExpansion.InitializeUserSecrets(TendrilHome, _logger);
                    ExpandSettingsVariables();
                    ExpandRepoPaths();
                    return true;
                }
            }
            catch
            {
                // Backup is also corrupt, fall through
            }
        }

        // Strategy 2: Create minimal valid config
        Settings = CreateMinimalSettings();
        var minimalYaml = YamlHelper.SerializerCompact.Serialize(Settings);
        FileHelper.WriteAllText(ConfigPath, minimalYaml);
        return true;
    }

    public void ResetToDefaults()
    {
        BackupBrokenConfig();
        Settings = CreateMinimalSettings();
        var yaml = YamlHelper.SerializerCompact.Serialize(Settings);
        FileHelper.WriteAllText(ConfigPath, yaml);
        ParseError = null;
        NeedsOnboarding = true;
    }

    public void RetryLoadConfig()
    {
        if (!File.Exists(ConfigPath)) return;

        try
        {
            var yaml = FileHelper.ReadAllText(ConfigPath);
            yaml = Regex.Replace(yaml, @"(?m)(?<=:\s+)(%\w+%.*)$", "'$1'");
            yaml = Regex.Replace(yaml, @"(?m)^(\s*-\s+)(%\w+%.*)$", "$1'$2'");
            Settings = YamlHelper.Deserializer.Deserialize<TendrilSettings>(yaml) ?? new TendrilSettings();
            MigrateProjectColors();
            CreateConfigBackup();
            ParseError = null;
            NeedsOnboarding = false;
            _levelNamesCache = null;
            VariableExpansion.InitializeUserSecrets(TendrilHome, _logger);
            ExpandSettingsVariables();
            ExpandRepoPaths();
            SettingsReloaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            BackupBrokenConfig();
            ParseError = new ConfigParseError(ex.Message, ConfigPath, ex);
        }
    }

    private void BackupBrokenConfig()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return;
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmss");
            var brokenPath = Path.Combine(
                Path.GetDirectoryName(ConfigPath)!,
                $"config.yaml.broken.{timestamp}.bak");
            File.Copy(ConfigPath, brokenPath, true);
        }
        catch
        {
            // Best-effort backup
        }
    }

    private void CreateConfigBackup()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return;
            var backupPath = ConfigPath + ".backup";
            File.Copy(ConfigPath, backupPath, true);
        }
        catch
        {
            // Best-effort backup
        }
    }

    private static TendrilSettings CreateMinimalSettings()
    {
        return new TendrilSettings
        {
            CodingAgent = "claude",
            Projects = new List<ProjectConfig>(),
            Verifications = new List<VerificationConfig>(),
            Levels = new List<LevelConfig>
            {
                new() { Name = "Bug", Badge = "Destructive" },
                new() { Name = "Critical", Badge = "Warning" },
                new() { Name = "NiceToHave", Badge = "Outline" },
                new() { Name = "Epic", Badge = "Info" }
            }
        };
    }

    private void ExpandRepoPaths()
    {
        if (Settings.Projects != null)
            foreach (var proj in Settings.Projects)
            {
                if (proj.Repos != null)
                    foreach (var repo in proj.Repos)
                        repo.Path = VariableExpansion.ExpandVariables(repo.Path, TendrilHome);

                for (var i = 0; i < proj.BuildDependencies.Count; i++)
                    proj.BuildDependencies[i] = VariableExpansion.ExpandVariables(proj.BuildDependencies[i], TendrilHome);
            }
    }

    internal static string? MigrateProjectColor(string? colorValue)
    {
        if (string.IsNullOrEmpty(colorValue))
            return null;

        if (Enum.TryParse<Colors>(colorValue, out _))
            return colorValue;

        return Colors.Slate.ToString();
    }

    private void MigrateProjectColors()
    {
        if (Settings?.Projects == null) return;

        var needsSave = false;
        foreach (var project in Settings.Projects)
        {
            var migrated = MigrateProjectColor(project.Color);
            var migratedStr = migrated ?? "";
            if (migratedStr != project.Color)
            {
                project.Color = migratedStr;
                needsSave = true;
            }
        }

        if (needsSave && File.Exists(ConfigPath))
        {
            var yaml = YamlHelper.SerializerCompact.Serialize(Settings);
            FileHelper.WriteAllText(ConfigPath, yaml);
        }
    }

    internal void SetTendrilHome(string tendrilHome)
    {
        TendrilHome = tendrilHome;
        ConfigPath = Path.Combine(TendrilHome, "config.yaml");

        // Load config if it exists at the new path
        if (File.Exists(ConfigPath))
        {
            try
            {
                var yaml = FileHelper.ReadAllText(ConfigPath);
                var loadedSettings = YamlHelper.Deserializer.Deserialize<TendrilSettings>(yaml);
                if (loadedSettings != null) Settings = loadedSettings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load Tendril config from new path {ConfigPath}", ConfigPath);
                ParseError = new ConfigParseError(ex.Message, ConfigPath, ex);
                BackupBrokenConfig();

                if (TryAutoHeal())
                {
                    ParseError = null;
                }
            }
        }

        MigrateProjectColors();
        _levelNamesCache = null;
        VariableExpansion.InitializeUserSecrets(TendrilHome, _logger);
        ExpandSettingsVariables();
    }

    /// <summary>
    ///     Expand variables in settings after loading config.
    /// </summary>
    private void ExpandSettingsVariables()
    {
        if (Settings == null) return;

        // Scalar expansions
        Settings.CodingAgent = ExpandVar(Settings.CodingAgent);
        Settings.PlanTemplate = ExpandVar(Settings.PlanTemplate);

        // Expand nested objects
        ExpandLlmConfig();
        ExpandEditorConfig();
        ExpandPromptwareConfigs();
        ExpandProjectConfigs();
        ExpandVerificationPrompts();
    }

    private string ExpandVar(string value) =>
        VariableExpansion.ExpandVariables(value, TendrilHome);

    private void ExpandLlmConfig()
    {
        if (Settings.Llm == null) return;
        Settings.Llm.Endpoint = ExpandVar(Settings.Llm.Endpoint);
        Settings.Llm.ApiKey = ExpandVar(Settings.Llm.ApiKey);
        Settings.Llm.Model = ExpandVar(Settings.Llm.Model);
    }

    private void ExpandEditorConfig()
    {
        if (Settings.Editor == null) return;
        Settings.Editor.Command = ExpandVar(Settings.Editor.Command);
        Settings.Editor.Label = ExpandVar(Settings.Editor.Label);
        Settings.Editor.IsAvailable = IsCommandAvailable(Settings.Editor.Command);
    }

    private void ExpandPromptwareConfigs()
    {
        if (Settings.Promptwares == null) return;
        foreach (var config in Settings.Promptwares.Values)
        {
            if (config.AllowedTools == null) continue;
            for (var i = 0; i < config.AllowedTools.Count; i++)
                config.AllowedTools[i] = ExpandVar(config.AllowedTools[i]);
        }
    }

    private void ExpandProjectConfigs()
    {
        if (Settings.Projects == null) return;
        foreach (var project in Settings.Projects)
        {
            project.Context = ExpandVar(project.Context);
            ExpandReviewActions(project.ReviewActions);
            ExpandHooks(project.Hooks);
        }
    }

    private void ExpandReviewActions(List<ReviewActionConfig>? actions)
    {
        if (actions == null) return;
        foreach (var action in actions)
        {
            action.Condition = ExpandVar(action.Condition);
            action.Action = ExpandVar(action.Action);
        }
    }

    private void ExpandHooks(List<PromptwareHookConfig>? hooks)
    {
        if (hooks == null) return;
        foreach (var hook in hooks)
        {
            hook.Condition = ExpandVar(hook.Condition);
            hook.Action = ExpandVar(hook.Action);
        }
    }

    private void ExpandVerificationPrompts()
    {
        if (Settings.Verifications == null) return;
        foreach (var verification in Settings.Verifications)
            verification.Prompt = ExpandVar(verification.Prompt);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        lock (_tempFilesLock)
        {
            foreach (var tempFile in _tempFiles)
            {
                try
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                        _logger.LogDebug("Deleted temp file: {TempFile}", tempFile);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temp file: {TempFile}", tempFile);
                }
            }

            _tempFiles.Clear();
        }
    }
}

public static class ProjectBadgeExtensions
{
    public static Badge WithProjectColor(this Badge badge, IConfigService config, string projectName)
    {
        var color = config.GetProjectColor(projectName);
        return color.HasValue ? badge.Color(color.Value) : badge;
    }
}
