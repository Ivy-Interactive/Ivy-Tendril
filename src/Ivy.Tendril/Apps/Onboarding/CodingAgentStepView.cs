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
                    (string.IsNullOrEmpty(check.LastError) ? "" : $"**Error details:**\n`{check.LastError}`\n\n") +
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
    private record ByoAgentInfo(string Key, string Label, Icons Logo);

    private static readonly AgentInfo[] Agents =
    [
        new("claude",   "Claude",   AgentBranding.IconFor("claude")),
        new("copilot",  "Copilot",  AgentBranding.IconFor("copilot")),
        new("codex",    "Codex",    AgentBranding.IconFor("codex")),
        new("gemini",   "Gemini",   AgentBranding.IconFor("gemini")),
        new("antigravity", "Antigravity", AgentBranding.IconFor("antigravity")),
        new("opencode", "OpenCode", AgentBranding.IconFor("opencode"))
    ];

    private static readonly ByoAgentInfo[] ByoAgents =
    [
        new("openaiproxy_card", "OpenAI", Icons.OpenAI),
        new("anthropic_card", "Anthropic", Icons.ClaudeCode),
        new("berget_card", "Berget AI", Icons.ChevronUp)
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

        var openAiProxyBaseUrl = UseState(
            GetOpenAiProxyBaseUrlFromConfig(config) is var url && !string.IsNullOrEmpty(url)
                ? url
                : "https://api.openai.com"
        );

        var openAiProxyApiKey = UseState(
            GetOpenAiProxyApiKeyFromConfig(config)
        );

        var (installDialog, showInstallDialog) = UseTrigger<InstallDialogArgs>((isOpen, args) =>
            new InstallMissingDialog(isOpen, args));

        var registeredAgents = agentRunner.RegisteredAgents;
        var visibleAgents = Agents.Where(a => registeredAgents.Contains(a.Key)).ToArray();
        var hasByoSupport = registeredAgents.Contains("openaiproxy") || registeredAgents.Contains("ivy");

        if (selectedAgent.Value is null)
        {
            return BuildPicker(visibleAgents, hasByoSupport, agentKey =>
            {
                selectedAgent.Set(agentKey);
                error.Set(null);
                if (agentKey != "openaiproxy_card" && agentKey != "anthropic_card" && agentKey != "berget_card")
                {
                    _ = RunFlowAsync(agentKey);
                }
            }, error.Value);
        }

        if (progressMessage.Value is null && (selectedAgent.Value == "openaiproxy_card" || selectedAgent.Value == "anthropic_card" || selectedAgent.Value == "berget_card"))
        {
            var isAnthropicCard = selectedAgent.Value == "anthropic_card";
            var isBergetCard = selectedAgent.Value == "berget_card";
            var cardTitle = isBergetCard ? "Setup Berget AI" : (isAnthropicCard ? "Setup Anthropic" : "Setup OpenAI");
            var defaultUrl = isBergetCard ? "https://api.berget.ai/v1" : (isAnthropicCard ? "https://api.anthropic.com/v1" : "https://api.openai.com");

            if (string.IsNullOrWhiteSpace(openAiProxyBaseUrl.Value) ||
                (isBergetCard && !openAiProxyBaseUrl.Value.Contains("api.berget.ai")) ||
                (isAnthropicCard && (openAiProxyBaseUrl.Value == "https://api.openai.com" || openAiProxyBaseUrl.Value.Contains("api.berget.ai"))) ||
                (!isAnthropicCard && !isBergetCard && (openAiProxyBaseUrl.Value.Contains("api.anthropic.com") || openAiProxyBaseUrl.Value.Contains("api.berget.ai"))))
            {
                openAiProxyBaseUrl.Set(defaultUrl);
            }

            object agentInputs = isBergetCard
                ? (Layout.Vertical().Width(Size.Auto().Max(Size.Units(120)))
                    | openAiProxyApiKey.ToPasswordInput("...")
                        .WithField()
                        .Label("API Key"))
                : (Layout.Vertical().Width(Size.Auto().Max(Size.Units(120)))
                    | openAiProxyBaseUrl.ToTextInput(defaultUrl)
                        .WithField()
                        .Label("API Base URL")
                    | openAiProxyApiKey.ToPasswordInput("sk-...")
                        .WithField()
                        .Label("API Key"));

            return Layout.Vertical().Margin(0, 0, 0, 2)
                   | Text.H3(cardTitle)
                   | (error.Value != null ? Text.Danger(error.Value) : null!)
                   | agentInputs
                   | (Layout.Horizontal().Margin(2, 0, 0, 0)
                       | new Button("Back")
                           .Ghost()
                           .OnClick(() =>
                           {
                               selectedAgent.Set(null);
                               error.Set(null);
                           })
                       | new Button("Continue")
                           .Primary()
                           .OnClick(() =>
                           {
                               if (string.IsNullOrWhiteSpace(openAiProxyApiKey.Value))
                               {
                                   error.Set("API Key is required.");
                                   return;
                               }

                               var baseUrl = isBergetCard || string.IsNullOrWhiteSpace(openAiProxyBaseUrl.Value)
                                   ? defaultUrl
                                   : openAiProxyBaseUrl.Value;

                               SaveOpenAiProxyBaseUrl(config, baseUrl);
                               SaveOpenAiProxyApiKey(config, openAiProxyApiKey.Value);
                               if (isBergetCard)
                               {
                                   SaveProfiles(config, "openaiproxy", "moonshotai/Kimi-K3", "moonshotai/Kimi-K3", "moonshotai/Kimi-K3");
                               }

                               config.SaveSettings();
                               _ = RunFlowAsync("openaiproxy");
                           })
                   );
        }

        var selectedLabel = Agents.FirstOrDefault(a => a.Key == selectedAgent.Value)?.Label
            ?? (selectedAgent.Value == "berget_card" ? "Berget AI"
            : (selectedAgent.Value == "openaiproxy_card" ? "OpenAI"
            : (selectedAgent.Value == "anthropic_card" ? "Anthropic"
            : (selectedAgent.Value == "openaiproxy" ? "OpenAI Proxy" : selectedAgent.Value))));

        return Layout.Vertical().Margin(0, 0, 0, 2)
               | Text.Block(progressMessage.Value ?? $"Setting Up {selectedLabel}")
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
                        error.Set("Please make sure your agent is present and you are authorized.");
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
            catch (Exception ex)
            {
                await progressCts.CancelAsync();
                progressValue.Set(null);
                progressMessage.Set(null);
                isStepLoading.Set(false);
                error.Set($"Please make sure your agent is present and you are authorized. ({ex.Message})");
                selectedAgent.Set(null);
            }
        }
    }

    private static object BuildPicker(
        AgentInfo[] agents,
        bool hasByoSupport,
        Action<string> onSelect,
        string? errorMessage)
    {
        var grid = Layout.Grid().Columns(3).Gap(2);

        grid = agents.Aggregate(grid, (current, a) =>
            current | new Card(
                Layout.Horizontal()
                    .Gap(2)
                    .AlignContent(Align.Center)
                    .Padding(0)
                | a.Logo.ToIcon().Width(Size.Px(32)).Height(Size.Px(32))
                | Text.Block(a.Label)
            ).OnClick(() => onSelect(a.Key)));

        object? byoSection = null;
        if (hasByoSupport)
        {
            var byoGrid = Layout.Grid().Columns(3).Gap(2);
            byoGrid = ByoAgents.Aggregate(byoGrid, (current, a) =>
                current | new Card(
                    Layout.Horizontal()
                        .Gap(2)
                        .AlignContent(Align.Center)
                        .Padding(0)
                    | a.Logo.ToIcon().Width(Size.Px(32)).Height(Size.Px(32))
                    | Text.Block(a.Label)
                ).OnClick(() => onSelect(a.Key)));

            byoSection = Layout.Vertical().Margin(2, 0, 0, 0)
                | Text.Block("Bring your own LLM").Bold()
                | byoGrid;
        }

        return Layout.Vertical().Margin(0, 0, 0, 2)
               | Text.H3("What is your coding agent?")
               | Text.Muted(
                   "Tendril is a coding orchestrator that runs on top of your own coding agent. Pick the agent you'd like to use:")
               | (errorMessage != null ? Text.Danger(errorMessage) : null!)
               | grid
               | (byoSection ?? null!);
    }

    private static List<SoftwareCheck> BuildChecks(IAgentRunner runner, string agentKey)
    {
        SoftwareCheck? gitCheck = null;
        gitCheck = new SoftwareCheck("Git", "git", "https://git-scm.com/downloads", true,
            async () =>
            {
                var (success, error) = await ProcessCheckHelper.TryCheckCommand("git", "--version");
                if (gitCheck != null) gitCheck.LastError = error;
                return success;
            });

        SoftwareCheck? pwshCheck = null;
        pwshCheck = new SoftwareCheck("PowerShell", "powershell", "https://github.com/PowerShell/PowerShell", true,
            async () =>
            {
                var (success, error) = await ProcessCheckHelper.CheckPowerShellWithDetails();
                if (pwshCheck != null) pwshCheck.LastError = error;
                return success;
            });

        SoftwareCheck? dotnetCheck = null;
        dotnetCheck = new SoftwareCheck(".NET 10 SDK", "dotnet", "https://dotnet.microsoft.com/download/dotnet/10.0", true,
            async () =>
            {
                var (success, error) = await ProcessCheckHelper.TryCheckCommand(PathHelper.GetDotnetPath(), "--version");
                if (dotnetCheck != null) dotnetCheck.LastError = error;
                return success;
            });

        return
        [
            gitCheck,
            pwshCheck,
            dotnetCheck,
            BuildAgentCheck(runner, agentKey)
        ];
    }

    private static SoftwareCheck BuildAgentCheck(IAgentRunner runner, string agentKey)
    {
        var healthCheck = runner.GetHealthCheck(agentKey);
        var info = healthCheck.GetOnboardingInfo();
        SoftwareCheck? agentCheck = null;
        agentCheck = new SoftwareCheck(info.DisplayName, agentKey, info.InstallUrl ?? "", true,
            async () =>
            {
                var status = await healthCheck.CheckInstallAsync();
                if (agentCheck != null) agentCheck.LastError = status.Error;
                return status.IsInstalled;
            },
            async () =>
            {
                var result = await healthCheck.CheckAuthAsync();
                return result.Status == AuthStatus.Authenticated
                    ? HealthCheckStatus.Authenticated
                    : HealthCheckStatus.NotAuthenticated;
            },
            info.SignInHint);
        return agentCheck;
    }

    private static string GetOpenAiProxyApiKeyFromConfig(IConfigService config)
    {
        var ac = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals("openaiproxy", StringComparison.OrdinalIgnoreCase));
        if (ac != null && ac.EnvironmentVariables.TryGetValue("ANTHROPIC_API_KEY", out var key))
            return key;
        return "";
    }

    private static void SaveOpenAiProxyApiKey(IConfigService config, string apiKey)
    {
        var ac = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals("openaiproxy", StringComparison.OrdinalIgnoreCase));

        if (ac == null)
        {
            ac = new AgentConfig { Name = "openaiproxy" };
            config.Settings.CodingAgents.Add(ac);
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            ac.EnvironmentVariables.Remove("ANTHROPIC_API_KEY");
        }
        else
        {
            ac.EnvironmentVariables["ANTHROPIC_API_KEY"] = apiKey;
        }
    }

    private static string GetOpenAiProxyBaseUrlFromConfig(IConfigService config)
    {
        var ac = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals("openaiproxy", StringComparison.OrdinalIgnoreCase));
        if (ac != null && ac.EnvironmentVariables.TryGetValue("ANTHROPIC_BASE_URL", out var url))
            return url;
        return "";
    }

    private static void SaveOpenAiProxyBaseUrl(IConfigService config, string url)
    {
        var ac = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals("openaiproxy", StringComparison.OrdinalIgnoreCase));

        if (ac == null)
        {
            ac = new AgentConfig { Name = "openaiproxy" };
            config.Settings.CodingAgents.Add(ac);
        }

        if (string.IsNullOrEmpty(url))
        {
            ac.EnvironmentVariables.Remove("ANTHROPIC_BASE_URL");
        }
        else
        {
            ac.EnvironmentVariables["ANTHROPIC_BASE_URL"] = url;
        }
    }

    private static void SaveProfiles(IConfigService config, string agentId, string deep, string balanced, string quick)
    {
        var ac = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals(agentId, StringComparison.OrdinalIgnoreCase));

        if (ac == null)
        {
            ac = new AgentConfig { Name = agentId };
            config.Settings.CodingAgents.Add(ac);
        }

        SetProfileModel(ac, "deep", deep);
        SetProfileModel(ac, "balanced", balanced);
        SetProfileModel(ac, "quick", quick);
    }

    private static void SetProfileModel(AgentConfig ac, string profileName, string model)
    {
        var profile = ac.Profiles.FirstOrDefault(p =>
            p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));

        if (profile == null)
        {
            profile = new AgentProfileConfig { Name = profileName };
            ac.Profiles.Add(profile);
        }

        profile.Model = model;
    }
}
