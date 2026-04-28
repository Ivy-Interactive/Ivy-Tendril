using Ivy.Tendril.Models;
using System.Diagnostics;
using Ivy.Helpers;

namespace Ivy.Tendril.Apps.Onboarding;

public class SoftwareCheckStepView(
    IState<int> stepperIndex,
    IState<Dictionary<string, bool>?> checkResults,
    IState<Dictionary<string, HealthCheckStatus?>?> healthResults) : ViewBase
{
    private static readonly List<SoftwareCheck> SoftwareChecks = new()
    {
        new("GitHub CLI", "gh", "https://cli.github.com/", true,
            () => CheckCommand("gh", "--version"),
            () => CheckHealth("gh", "auth status"),
            "Run `gh auth login` to authenticate"),
        new("Claude CLI", "claude", "https://docs.anthropic.com/en/docs/claude-code", false,
            () => CheckCommand("claude", "--version"),
            () => CheckHealth("claude", "-p \"ping\" --max-turns 1"),
            "Run `claude` to log in, or check your plan/credits"),
        new("Codex CLI", "codex", "https://openai.com/index/codex/", false,
            () => CheckCommand("codex", "--version"),
            () => CheckHealth("codex", "login status"),
            "Run `codex login` to authenticate"),
        new("Gemini CLI", "gemini", "https://github.com/google-gemini/gemini-cli", false,
            () => CheckCommand("gemini", "--version"),
            CheckGeminiAuth,
            "Run `gemini` to authenticate (opens browser). Verify auth before selecting Gemini as your coding agent."),
        new("Git", "git", "https://git-scm.com/downloads", true,
            () => CheckCommand("git", "--version")),
        new("PowerShell", "powershell", "https://github.com/PowerShell/PowerShell", true,
            CheckPowerShell),
        new("Pandoc (Optional)", "pandoc", "https://pandoc.org/installing.html", false,
            () => CheckCommand("pandoc", "--version"))
    };

    public override object Build()
    {
        var isChecking = UseState(false);

        var hasAnyCodingAgent = checkResults.Value != null
                                && (checkResults.Value["claude"] || checkResults.Value["codex"] ||
                                    checkResults.Value["gemini"]);

        var ghHealthy = healthResults.Value?.GetValueOrDefault("gh") == HealthCheckStatus.Authenticated;
        var anyAgentHealthy = healthResults.Value != null
                              && (healthResults.Value.GetValueOrDefault("claude") == HealthCheckStatus.Authenticated
                                  || healthResults.Value.GetValueOrDefault("codex") == HealthCheckStatus.Authenticated
                                  || healthResults.Value.GetValueOrDefault("gemini") == HealthCheckStatus.Authenticated);

        var allRequiredPassed = checkResults.Value != null
                                && checkResults.Value["gh"] && ghHealthy
                                && hasAnyCodingAgent && anyAgentHealthy
                                && checkResults.Value["git"]
                                && checkResults.Value["powershell"];

        return Layout.Vertical().Margin(0, 0, 0, 20)
               | Text.H2("Required Software")
               | Text.Markdown(
                   """
                   Tendril requires the following software to be installed:

                   **Required:**
                   - **Coding Agent** - At least one of: Claude Code CLI, Codex CLI, or Gemini CLI
                   - **GitHub CLI** - For PR creation and GitHub integration
                   - **Git** - For version control
                   - **PowerShell** - For running promptware and hooks

                   **Optional:**
                   - **Pandoc** - For PDF export functionality
                   """)
               | (checkResults.Value != null
                   ? Layout.Vertical()
                     | new Separator()
                     | (Layout.Horizontal()
                        | Text.H3("Results")
                        | new Spacer()
                        | new Button(!isChecking.Value ? "Recheck" : "Checking...")
                            .Outline()
                            .Small()
                            .Icon(Icons.RefreshCw, Align.Right)
                            .Loading(isChecking.Value)
                            .Disabled(isChecking.Value)
                            .OnClick(async () => await CheckSoftware()))
                     | new TableBuilder<SoftwareRow>(
                         SoftwareChecks
                             .Select(check => new SoftwareCheckResult(
                                 check.Name,
                                 check.Key,
                                 checkResults.Value[check.Key],
                                 healthResults.Value?.GetValueOrDefault(check.Key),
                                 check.InstallUrl,
                                 check.IsRequired))
                             .Select(MakeSoftwareRow)
                             .ToArray())
                         .Builder(t => t.Instructions, f => f.Func<SoftwareRow, string>(value =>
                             value.StartsWith("http") ? new Button("Install").Inline().Url(value) : (object)value))
                         .Width(Size.Full())
                   : null!)
               | (checkResults.Value == null
                   ? new Button("Check Software")
                       .Primary()
                       .Large()
                       .Icon(Icons.ArrowRight, Align.Right)
                       .Loading(isChecking.Value)
                       .Disabled(isChecking.Value)
                       .OnClick(async () => await CheckSoftware())
                   : allRequiredPassed
                       ? new Button("Continue")
                           .Primary()
                           .Large()
                           .Icon(Icons.ArrowRight, Align.Right)
                           .Disabled(isChecking.Value)
                           .OnClick(() => stepperIndex.Set(stepperIndex.Value + 1))
                       : Text.Muted("Please Wait...")
               );

        async Task CheckSoftware()
        {
            isChecking.Set(true);

            var installTasks = SoftwareChecks.Select(s => s.InstallCheck()).ToList();
            await Task.WhenAll(installTasks);

            var results = SoftwareChecks
                .Zip(installTasks, (check, task) => (check, installed: task.Result))
                .ToDictionary(x => x.check.Key, x => x.installed);

            checkResults.Set(results);

            var healthTasks = SoftwareChecks
                .Where(s => s.HealthCheck != null && results[s.Key])
                .Select(s => (s.Key, Task: s.HealthCheck!()))
                .ToList();

            var health = new Dictionary<string, HealthCheckStatus?>();

            foreach (var (key, task) in healthTasks)
            {
                health[key] = await task;
                healthResults.Set(new Dictionary<string, HealthCheckStatus?>(health));
            }

            isChecking.Set(false);
        }
    }

    private static SoftwareRow MakeSoftwareRow(SoftwareCheckResult result)
    {
        string statusText = result switch
        {
            { IsInstalled: false, IsRequired: true } => "❌ Not Found",
            { IsInstalled: false } => "❌ Not Installed",
            { HealthStatus: HealthCheckStatus.Authenticated } => "✅ Ready",
            { HealthStatus: HealthCheckStatus.NotAuthenticated } => "⚠️ Installed but not authenticated",
            { HealthStatus: HealthCheckStatus.CheckFailed } => "⚠️ Health check failed",
            _ => "✅ Installed"
        };

        var check = SoftwareChecks.FirstOrDefault(c => c.Key == result.Key);
        string instructions = result switch
        {
            { IsInstalled: false } => result.InstallUrl,
            { HealthStatus: HealthCheckStatus.NotAuthenticated } => check?.HealthHint ?? "",
            { HealthStatus: HealthCheckStatus.CheckFailed } => "Try clicking Recheck",
            _ => ""
        };

        return new SoftwareRow(result.DisplayName, statusText, instructions);
    }

    private record SoftwareRow(string Software, string Status, string Instructions);

    private static async Task<bool> CheckPowerShell()
    {
        return await CheckCommand("pwsh", "-Version") || await CheckCommand("powershell", "-Version");
    }

    private static Task<bool> CheckCommand(string fileName, string arguments)
        => CheckProcess(fileName, arguments, 10000);

    private static async Task<HealthCheckStatus> CheckHealth(string fileName, string arguments)
    {
        try
        {
            return await Task.Run(() =>
            {
                var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = OperatingSystem.IsWindows() ? "cmd.exe" : fileName,
                    Arguments = OperatingSystem.IsWindows() ? $"/S /c \"{fileName} {arguments}\"" : arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (proc is null) return HealthCheckStatus.CheckFailed;
                var exited = proc.WaitForExitOrKill(30000);
                if (!exited) return HealthCheckStatus.CheckFailed;
                return proc.ExitCode == 0
                    ? HealthCheckStatus.Authenticated
                    : HealthCheckStatus.NotAuthenticated;
            });
        }
        catch
        {
            return HealthCheckStatus.CheckFailed;
        }
    }

    private static Task<HealthCheckStatus> CheckGeminiAuth()
    {
        return Task.Run(() =>
        {
            try
            {
                var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var oauthCredsPath = Path.Combine(homeDir, ".gemini", "oauth_creds.json");

                if (File.Exists(oauthCredsPath))
                {
                    var fileInfo = new FileInfo(oauthCredsPath);
                    return fileInfo.Length > 0
                        ? HealthCheckStatus.Authenticated
                        : HealthCheckStatus.NotAuthenticated;
                }

                return HealthCheckStatus.NotAuthenticated;
            }
            catch
            {
                return HealthCheckStatus.CheckFailed;
            }
        });
    }

    private static async Task<bool> CheckProcess(string fileName, string arguments, int timeoutMs)
    {
        try
        {
            return await Task.Run(() =>
            {
                var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = OperatingSystem.IsWindows() ? "cmd.exe" : fileName,
                    Arguments = OperatingSystem.IsWindows() ? $"/S /c \"{fileName} {arguments}\"" : arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                proc.WaitForExitOrKill(timeoutMs);
                return proc?.ExitCode == 0;
            });
        }
        catch
        {
            return false;
        }
    }
}
