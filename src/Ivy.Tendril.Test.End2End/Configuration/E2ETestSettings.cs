namespace Ivy.Tendril.Test.End2End.Configuration;

public class E2ETestSettings
{
    public string Agent { get; set; } = "claude";
    public string TestRepo { get; set; } = "Ivy-Interactive/Ivy-Templates";
    public string TendrilProjectPath { get; set; } = "../Ivy.Tendril/Ivy.Tendril.csproj";
    public int StartupTimeoutSeconds { get; set; } = 60;
    public int PlanExecutionTimeoutSeconds { get; set; } = 600;
    public bool Headless { get; set; } = true;
    public int SlowMo { get; set; }
    public bool CleanupFork { get; set; } = true;
    public bool ScreenshotOnFailure { get; set; } = true;
}
