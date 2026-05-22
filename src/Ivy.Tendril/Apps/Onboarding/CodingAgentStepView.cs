using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Apps.Onboarding.Models;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Onboarding;

internal record InstallDialogArgs(SoftwareCheck Check, TaskCompletionSource<bool> Tcs);

internal class InstallMissingDialog(
    IState<bool> isOpen,
    InstallDialogArgs args) : ViewBase
{
    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var check = args.Check;

        void Close(bool result)
        {
            args.Tcs.TrySetResult(result);
            isOpen.Set(false);
        }

        return new Dialog(
            _ => Close(false),
            new DialogHeader($"{check.Name} is required"),
            new DialogBody(
                Text.Markdown(
                    $"Tendril needs **{check.Name}** but it isn't installed.\n\n" +
                    $"Click **Install** to open the install page, then click **OK** once you've installed it.")),
            new DialogFooter(
                new Button("Cancel")
                    .Ghost()
                    .OnClick(() => Close(false)),
                new Button("Install")
                    .Outline()
                    .Icon(Icons.ExternalLink, Align.Right)
                    .OnClick(() => client.OpenUrl(check.InstallUrl)),
                new Button("OK")
                    .Primary()
                    .OnClick(() => Close(true))
            )
        ).Width(Size.Rem(28));
    }
}

public class CodingAgentStepView(
    IState<int> stepperIndex,
    IState<bool> commonChecksPassed,
    IState<string?> completedAgentKey,
    IState<bool> isStepLoading) : ViewBase
{
    private record AgentInfo(string Key, string Label, Icons Logo);

    private static readonly AgentInfo[] Agents =
    [
        new("claude",   "Claude",   Icons.ClaudeCode),
        new("copilot",  "Copilot",  Icons.Copilot),
        new("codex",    "Codex",    Icons.OpenAI),
        new("antigravity", "Antigravity", Icons.Antigravity),
        new("opencode", "OpenCode", Icons.OpenCode)
    ];

    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();
        var agentRunner = UseService<IAgentRunner>();

        var selectedAgent = UseState<string?>(null);
        var progressMessage = UseState<string?>(null);
        var progressValue = UseState<int?>(null);
        var authCode = UseState<string?>(null);
        var error = UseState<string?>(null);

        var (installDialog, showInstallDialog) = UseTrigger<InstallDialogArgs>((isOpen, args) =>
            new InstallMissingDialog(isOpen, args));

        if (selectedAgent.Value is null)
        {
            return BuildPicker(agentKey =>
            {
                selectedAgent.Set(agentKey);
                _ = RunFlowAsync(agentKey);
            }, error.Value);
        }

        var selected = Agents.First(a => a.Key == selectedAgent.Value);

        return Layout.Vertical().Margin(0, 0, 0, 20)
               | Text.Block(progressMessage.Value ?? $"Setting Up {selected.Label}")
               | (progressValue.Value != null
                   ? new Progress(progressValue.Value.Value)
                   : null!)
               | (authCode.Value != null
                   ? Text.Markdown($"**Device code:** `{authCode.Value}` — enter this in your browser if prompted.")
                   : null!)
               | (error.Value != null ? Text.Danger(error.Value) : null!)
               | installDialog;

        async Task RunFlowAsync(string agentKey)
        {
            error.Set(null);
            authCode.Set(null);

            if (completedAgentKey.Value == agentKey)
            {
                stepperIndex.Set(stepperIndex.Value + 1);
                return;
            }

            isStepLoading.Set(true);
            var progressCts = new CancellationTokenSource();
            _ = UxHelper.AnimateProgressAsync(progressValue, progressCts.Token);

            try
            {
                var checks = commonChecksPassed.Value
                    ? [BuildAgentCheck(agentRunner, agentKey)]
                    : BuildChecks(agentRunner, agentKey);

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

                    await progressCts.CancelAsync();
                    progressValue.Set(null);
                    progressMessage.Set(null);

                    var tcs = new TaskCompletionSource<bool>();
                    showInstallDialog(new InstallDialogArgs(missing, tcs));
                    var resumed = await tcs.Task;

                    if (!resumed)
                    {
                        isStepLoading.Set(false);
                        selectedAgent.Set(null);
                        return;
                    }

                    progressCts = new CancellationTokenSource();
                    _ = UxHelper.AnimateProgressAsync(progressValue, progressCts.Token);
                }

                foreach (var c in checks.Where(c => c.HealthCheck != null))
                {
                    progressMessage.Set($"Verifying {c.Name} Authentication...");
                    var status = await c.HealthCheck!();
                    if (status == HealthCheckStatus.Authenticated) continue;

                    progressMessage.Set($"Signing In to {c.Name}... (Browser Will Open)");
                    authCode.Set(null);

                    var hc = agentRunner.GetHealthCheck(c.Key);
                    var callbacks = new AuthFlowCallbacks
                    {
                        OnUrl = url => { client.OpenUrl(url); return Task.CompletedTask; },
                        OnCode = code => authCode.Set(code),
                    };
                    await hc.RunAuthFlowAsync(callbacks, CancellationToken.None);
                    authCode.Set(null);

                    progressMessage.Set($"Verifying {c.Name} Authentication...");
                    status = await c.HealthCheck!();
                    if (status != HealthCheckStatus.Authenticated)
                    {
                        await progressCts.CancelAsync();
                        progressValue.Set(null);
                        progressMessage.Set(null);
                        isStepLoading.Set(false);
                        error.Set($"Could not authenticate {c.Name}. Please try again.");
                        selectedAgent.Set(null);
                        return;
                    }
                }

                commonChecksPassed.Set(true);

                config.Settings.CodingAgent = agentKey;
                config.SetPendingCodingAgent(agentKey);

                completedAgentKey.Set(agentKey);

                await progressCts.CancelAsync();
                progressValue.Set(100);
                progressMessage.Set("Done");
                await Task.Delay(250); // no token — progressCts is already cancelled

                progressValue.Set(null);
                progressMessage.Set(null);
                isStepLoading.Set(false);
                stepperIndex.Set(stepperIndex.Value + 1);
            }
            catch
            {
                await progressCts.CancelAsync();
                progressValue.Set(null);
                progressMessage.Set(null);
                isStepLoading.Set(false);
                throw;
            }
        }
    }

    private static object BuildPicker(Action<string> onSelect, string? errorMessage)
    {
        var grid = Layout.Grid().Columns(3).Gap(2);
        
        grid = Agents.Aggregate(grid, (current, a) => current | new Card(Layout.Horizontal().Gap(2).AlignContent(Align.Center).Padding(0) | a.Logo.ToIcon().Width(Size.Px(32)).Height(Size.Px(32)) | Text.Block(a.Label)).OnClick(() => onSelect(a.Key)));

        return Layout.Vertical().Margin(0, 0, 0, 20)
               | Text.H3("What is your coding agent?")
               | Text.Muted(
                   "Tendril is a coding orchestrator that runs on top of your own coding agent. Pick the agent you'd like to use:")
               | (errorMessage != null ? Text.Danger(errorMessage) : null!)
               | grid;
    }

    private static List<SoftwareCheck> BuildChecks(IAgentRunner runner, string agentKey) =>
    [
        new("Git", "git", "https://git-scm.com/downloads", true,
            () => ProcessCheckHelper.CheckCommand("git", "--version")),
        new("PowerShell", "powershell", "https://github.com/PowerShell/PowerShell", true,
            ProcessCheckHelper.CheckPowerShell),
        BuildAgentCheck(runner, agentKey)
    ];

    private static SoftwareCheck BuildAgentCheck(IAgentRunner runner, string agentKey)
    {
        var healthCheck = runner.GetHealthCheck(agentKey);
        var info = healthCheck.GetOnboardingInfo();
        return new SoftwareCheck(info.DisplayName, agentKey, info.InstallUrl ?? "", true,
            async () => (await healthCheck.CheckInstallAsync()).IsInstalled,
            async () =>
            {
                var result = await healthCheck.CheckAuthAsync();
                return result.Status == AuthStatus.Authenticated
                    ? HealthCheckStatus.Authenticated
                    : HealthCheckStatus.NotAuthenticated;
            },
            info.SignInHint);
    }

}
