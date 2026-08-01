namespace Ivy.Tendril.Services;

public interface IConfigService : IDisposable
{
    TendrilSettings Settings { get; }
    string TendrilHome { get; }
    string ConfigPath { get; }
    string PlanFolder { get; }
    List<ProjectConfig> Projects { get; }
    List<LevelConfig> Levels { get; }
    string[] LevelNames { get; }
    EditorConfig Editor { get; }
    bool NeedsOnboarding { get; }
    ConfigParseError? ParseError { get; }

    ProjectConfig? GetProject(string name);
    bool TryAutoHeal();
    void ResetToDefaults();
    void RetryLoadConfig();
    Colors? GetLevelColor(string level);
    Colors? GetProjectColor(string projectName);
    void SaveSettings();

    /// <summary>Reloads, mutates and saves config.yaml under a cross-process lock. Use from CLI/MCP instead of <see cref="SaveSettings"/>.</summary>
    void MutateAndSave(Action<TendrilSettings> mutate);

    void ReloadSettings();
    event EventHandler? SettingsReloaded;
    void SetPendingTendrilHome(string path);
    string? GetPendingTendrilHome();
    void SetPendingProject(ProjectConfig project);
    ProjectConfig? GetPendingProject();
    void SetPendingCodingAgent(string name);
    string? GetPendingCodingAgent();
    void SetPendingVerificationDefinitions(List<VerificationConfig> definitions);
    List<VerificationConfig>? GetPendingVerificationDefinitions();
    void CompleteOnboarding(string tendrilHome);
    void OpenInEditor(string path);
    string PolishMarkdown(string content);
}
