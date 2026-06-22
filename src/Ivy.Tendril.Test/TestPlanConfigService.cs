using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

internal class TestPlanConfigService : IConfigService
{
    private readonly List<ProjectConfig> _projects;

    public TestPlanConfigService(string repoDir, string projectName = "TestProject")
    {
        _projects =
        [
            new ProjectConfig
            {
                Name = projectName,
                Repos = [new RepoRef { Path = repoDir }]
            }
        ];
    }

    public TendrilSettings Settings => new() { Projects = _projects };
    public string TendrilHome => "";
    public string ConfigPath => "";
    public string PlanFolder => "";
    public List<ProjectConfig> Projects => _projects;
    public List<LevelConfig> Levels => [];
    public string[] LevelNames => [];
    public EditorConfig Editor => new();
    public bool NeedsOnboarding => false;
    public ConfigParseError? ParseError => null;
#pragma warning disable CS0067
    public event EventHandler? SettingsReloaded;
#pragma warning restore CS0067

    public ProjectConfig? GetProject(string name) =>
        _projects.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public bool TryAutoHeal() => false;
    public void ResetToDefaults() { }
    public void RetryLoadConfig() { }
    public Colors? GetLevelColor(string level) => null;
    public Colors? GetProjectColor(string projectName) => null;
    public void SaveSettings() { }
    public void ReloadSettings() { }
    public void SetPendingTendrilHome(string path) { }
    public string? GetPendingTendrilHome() => null;
    public void SetPendingProject(ProjectConfig project) { }
    public ProjectConfig? GetPendingProject() => null;
    public void SetPendingCodingAgent(string name) { }
    public string? GetPendingCodingAgent() => null;
    public void SetPendingVerificationDefinitions(List<VerificationConfig> definitions) { }
    public List<VerificationConfig>? GetPendingVerificationDefinitions() => null;
    public void CompleteOnboarding(string tendrilHome) { }
    public void OpenInEditor(string path) { }
    public string PreprocessForEditing(string path) => path;
}
