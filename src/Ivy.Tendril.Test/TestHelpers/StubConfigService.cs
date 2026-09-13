using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test.TestHelpers;

public class StubConfigService(List<ProjectConfig>? projects = null) : IConfigService
{
    public TendrilSettings Settings => new();
    public string TendrilHome => "";
    public string ConfigPath => "";
    public string PlanFolder => "";
    public List<ProjectConfig> Projects => projects ?? [];
    public List<LevelConfig> Levels => [];
    public string[] LevelNames => [];
    public EditorConfig Editor => new() { Command = "code", Label = "VS Code" };
    public bool NeedsOnboarding => false;
    public ConfigParseError? ParseError => null;

    public ProjectConfig? GetProject(string name)
    {
        return Projects.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public Colors? GetLevelColor(string level)
    {
        return null;
    }

    public Colors? GetProjectColor(string projectName)
    {
        var colorStr = GetProject(projectName)?.Color;
        return !string.IsNullOrEmpty(colorStr) && Enum.TryParse<Colors>(colorStr, ignoreCase: true, out var c) ? c : null;
    }

    public void SaveSettings()
    {
    }

    public void MutateAndSave(Action<TendrilSettings> mutate)
    {
        mutate(Settings);
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

    public string PolishMarkdown(string content)
    {
        return content;
    }

    public void Dispose()
    {
    }
}
