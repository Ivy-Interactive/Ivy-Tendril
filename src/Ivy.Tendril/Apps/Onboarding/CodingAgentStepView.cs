using System.Diagnostics;
using Ivy.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Onboarding;

public class CodingAgentStepView(IState<int> stepperIndex) : ViewBase
{
    private record AgentInfo(string Key, string Label, Icons Logo);

    private static readonly AgentInfo[] Agents =
    [
        new("claude",  "Claude",  Icons.ClaudeCode),
        new("codex",   "Codex",   Icons.OpenAI),
        new("gemini",  "Gemini",  Icons.Gemini),
        new("copilot", "Copilot", Icons.Copilot)
    ];

    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();
        var authRunner = UseService<IOnboardingAuthRunner>();

        var selectedAgent = UseState<string?>(null);
        var progressMessage = UseState<string?>(null);
        var progressValue = UseState<int?>(null);
        var missingCheck = UseState<SoftwareCheck?>(null);
        var pendingDialogTcs = UseState<TaskCompletionSource<bool>?>(null);
        var authCode = UseState<string?>(null);
        var error = UseState<string?>(null);

        async Task RunFlowAsync(string agentKey)
        {
            error.Set(null);
            authCode.Set(null);

            var progressCts = new CancellationTokenSource();
            _ = DriveProgressAsync(progressValue, progressCts.Token);

            try
            {
                var checks = BuildChecks(agentKey);

                while (true)
                {
                    SoftwareCheck? missing = null;
                    foreach (var c in checks)
                    {
                        progressMessage.Set($"Checking {c.Name}...");
                        if (!await c.InstallCheck())
                        {
                            missing = c;
                            break;
                        }
                    }

                    if (missing is null) break;

                    // Pause the progress bar while the install-missing dialog is up
                    progressCts.Cancel();
                    progressValue.Set(null);
                    progressMessage.Set(null);

                    var tcs = new TaskCompletionSource<bool>();
                    pendingDialogTcs.Set(tcs);
                    missingCheck.Set(missing);
                    var resumed = await tcs.Task;
                    pendingDialogTcs.Set(null);
                    missingCheck.Set(null);

                    if (!resumed)
                    {
                        selectedAgent.Set(null);
                        return;
                    }

                    // Resume progress for the next pass
                    progressCts = new CancellationTokenSource();
                    _ = DriveProgressAsync(progressValue, progressCts.Token);
                }

                foreach (var c in checks.Where(c => c.HealthCheck != null))
                {
                    progressMessage.Set($"Verifying {c.Name} authentication...");
                    var status = await c.HealthCheck!();
                    if (status == HealthCheckStatus.Authenticated) continue;

                    progressMessage.Set($"Signing in to {c.Name}... (browser will open)");
                    authCode.Set(null);
                    await authRunner.RunAuthAsync(c.Key, client, code => authCode.Set(code), CancellationToken.None);
                    authCode.Set(null);

                    progressMessage.Set($"Verifying {c.Name} authentication...");
                    status = await c.HealthCheck!();
                    if (status != HealthCheckStatus.Authenticated)
                    {
                        progressCts.Cancel();
                        progressValue.Set(null);
                        progressMessage.Set(null);
                        error.Set($"Could not authenticate {c.Name}. Please try again.");
                        selectedAgent.Set(null);
                        return;
                    }
                }

                progressCts.Cancel();
                progressValue.Set(100);
                progressMessage.Set("Done");
                await Task.Delay(250);

                config.Settings.CodingAgent = agentKey;
                config.SetPendingCodingAgent(agentKey);
                progressValue.Set(null);
                progressMessage.Set(null);
                stepperIndex.Set(stepperIndex.Value + 1);
            }
            catch
            {
                progressCts.Cancel();
                progressValue.Set(null);
                progressMessage.Set(null);
                throw;
            }
        }

        if (selectedAgent.Value is null)
        {
            return BuildPicker(agentKey =>
            {
                selectedAgent.Set(agentKey);
                _ = RunFlowAsync(agentKey);
            }, error.Value);
        }

        var selected = Agents.First(a => a.Key == selectedAgent.Value);
        var missing = missingCheck.Value;

        return Layout.Vertical().Margin(0, 0, 0, 20).Gap(4)
               | Text.Bold(progressMessage.Value ?? $"Setting up {selected.Label}")
               | (progressValue.Value != null
                   ? new Progress(progressValue.Value.Value)
                   : null!)
               | (authCode.Value != null
                   ? Text.Markdown($"**Device code:** `{authCode.Value}` — enter this in your browser if prompted.")
                   : null!)
               | (error.Value != null ? Text.Danger(error.Value) : null!)
               | (missing != null
                   ? new Dialog(
                       _ => pendingDialogTcs.Value?.TrySetResult(false),
                       new DialogHeader($"{missing.Name} is required"),
                       new DialogBody(
                           Text.Markdown(
                               $"Tendril needs **{missing.Name}** but it isn't installed.\n\n" +
                               $"Click **Install** to open the install page, then click **OK** once you've installed it.")),
                       new DialogFooter(
                           new Button("Cancel")
                               .Ghost()
                               .OnClick(() => pendingDialogTcs.Value?.TrySetResult(false)),
                           new Button("Install")
                               .Outline()
                               .Icon(Icons.ExternalLink, Align.Right)
                               .OnClick(() => client.OpenUrl(missing.InstallUrl)),
                           new Button("OK")
                               .Primary()
                               .OnClick(() => pendingDialogTcs.Value?.TrySetResult(true))
                       )
                   ).Width(Size.Rem(28))
                   : null!);
    }

    private static object BuildPicker(Action<string> onSelect, string? errorMessage)
    {
        var grid = Layout.Grid().Columns(2).Gap(2);
        foreach (var a in Agents)
        {
            grid |= new Card(
                Layout.Horizontal().Gap(2).AlignContent(Align.Center).Padding(1)
                | a.Logo.ToIcon().Width(Size.Px(32)).Height(Size.Px(32))
                | Text.Block(a.Label)
            ).OnClick(() => onSelect(a.Key));
        }

        return Layout.Vertical().Margin(0, 0, 0, 20).Gap(4)
               | Text.H1("Welcome to Ivy Tendril")
               | Text.Markdown(
                   "Tendril is a coding orchestrator powered by AI agents. Pick the agent you'd like to use — we'll check the required software and sign you in.")
               | (errorMessage != null ? Text.Danger(errorMessage) : null!)
               | grid
               | Text.Markdown(
                   """
                   >[!NOTE]
                   >Tendril runs agents at scale and can consume tokens rapidly.
                   """);
    }

    private static List<SoftwareCheck> BuildChecks(string agentKey)
    {
        var list = new List<SoftwareCheck>
        {
            new("Git", "git", "https://git-scm.com/downloads", true,
                () => CheckCommand("git", "--version")),
            new("PowerShell", "powershell", "https://github.com/PowerShell/PowerShell", true,
                CheckPowerShell),
            new("GitHub CLI", "gh", "https://cli.github.com/", true,
                () => CheckCommand("gh", "--version"),
                () => CheckHealth("gh", "auth status --active"),
                "Sign in to GitHub")
        };

        switch (agentKey)
        {
            case "claude":
                list.Add(new("Claude CLI", "claude", "https://docs.anthropic.com/en/docs/claude-code", true,
                    () => CheckCommand("claude", "--version"),
                    () => CheckHealth("claude", "-p \"ping\" --max-turns 1"),
                    "Sign in to Claude"));
                break;
            case "codex":
                list.Add(new("Codex CLI", "codex", "https://openai.com/index/codex/", true,
                    () => CheckCommand("codex", "--version"),
                    () => CheckHealth("codex", "login status"),
                    "Sign in to Codex"));
                break;
            case "gemini":
                list.Add(new("Gemini CLI", "gemini", "https://github.com/google-gemini/gemini-cli", true,
                    () => CheckCommand("gemini", "--version"),
                    CheckGeminiAuth,
                    "Sign in to Gemini"));
                break;
            case "copilot":
                list.Add(new("Copilot CLI", "copilot", "https://githubnext.com/projects/copilot-cli", true,
                    () => CheckCommand("copilot", "--version"),
                    () => CheckHealth("copilot", "-p \"ping\" --allow-all -s"),
                    "Sign in to Copilot"));
                break;
        }

        return list;
    }

    private static async Task DriveProgressAsync(IState<int?> value, CancellationToken ct)
    {
        // Asymptotic curve: starts fast, slows toward ~92%. Real completion overrides to 100%.
        value.Set(0);
        double current = 0;
        const double ceiling = 92.0;
        while (!ct.IsCancellationRequested)
        {
            var remaining = ceiling - current;
            var step = remaining * 0.06 + 0.4;
            current = Math.Min(ceiling - 0.5, current + step);
            value.Set((int)Math.Round(current));
            try { await Task.Delay(150, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private static async Task<bool> CheckPowerShell()
        => await CheckCommand("pwsh", "-Version") || await CheckCommand("powershell", "-Version");

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
