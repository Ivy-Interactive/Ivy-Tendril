using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test.TestHelpers;

public class StubConfigService : IConfigService
{
    public TendrilSettings Settings => new();
    public string TendrilHome => "";
    public string ConfigPath => "";
    public string PlanFolder => "";
    public List<ProjectConfig> Projects => [];
    public List<LevelConfig> Levels => [];
    public string[] LevelNames => [];
    public EditorConfig Editor => new() { Command = "code", Label = "VS Code" };
    public bool NeedsOnboarding => false;
    public ConfigParseError? ParseError => null;

    public ProjectConfig? GetProject(string name)
    {
        return null;
    }

    public BadgeVariant GetBadgeVariant(string level)
    {
        return BadgeVariant.Outline;
    }

    public Colors? GetProjectColor(string projectName)
    {
        return null;
    }

    public void SaveSettings()
    {
    }

    public void ReloadSettings()
    {
    }

    public bool TryAutoHeal()
    {
        return false;
    }

    public void ResetToDefaults()
    {
    }

    public void RetryLoadConfig()
    {
    }
#pragma warning disable CS0067
    public event EventHandler? SettingsReloaded;
#pragma warning restore CS0067
    public void SetPendingCodingAgent(string name)
    {
    }

    public string? GetPendingCodingAgent()
    {
        return null;
    }

    public void SetPendingTendrilHome(string path)
    {
    }

    public string? GetPendingTendrilHome()
    {
        return null;
    }

    public void SetPendingProject(ProjectConfig project)
    {
    }

    public ProjectConfig? GetPendingProject()
    {
        return null;
    }

    public void SetPendingVerificationDefinitions(List<VerificationConfig> definitions)
    {
    }

    public List<VerificationConfig>? GetPendingVerificationDefinitions()
    {
        return null;
    }

    public void CompleteOnboarding(string tendrilHome)
    {
    }

    public void OpenInEditor(string path)
    {
    }

    public string PreprocessForEditing(string path)
    {
        return path;
    }
}