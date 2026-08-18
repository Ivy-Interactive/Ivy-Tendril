using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.OpenAiProxy;
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

        var deepModel = UseState("default");
        var balancedModel = UseState("default");
        var quickModel = UseState("default");
        var lastDetectedProvider = UseState<string?>(null);

        var byoSubStep = UseState(0);
        var isFetchingModels = UseState(false);
        var fetchedModels = UseState<IReadOnlyList<ModelInfo>?>(null);
        var isTestingModels = UseState(false);
        var apiKeyError = UseState<string?>(null);
        var baseUrlError = UseState<string?>(null);
        var deepModelError = UseState<string?>(null);
        var balancedModelError = UseState<string?>(null);
        var quickModelError = UseState<string?>(null);
        var testSuccessMessage = UseState<string?>(null);
        var generalError = UseState<string?>(null);
        var customDeepText = UseState("");
        var customBalancedText = UseState("");
        var customQuickText = UseState("");

        var (installDialog, showInstallDialog) = UseTrigger<InstallDialogArgs>((isOpen, args) =>
            new InstallMissingDialog(isOpen, args));

        var registeredAgents = agentRunner.RegisteredAgents;
        var visibleAgents = Agents.Where(a => registeredAgents.Contains(a.Key)).ToArray();

        if (selectedAgent.Value is null)
        {
            return BuildPicker(visibleAgents, agentKey =>
            {
                selectedAgent.Set(agentKey);
                byoSubStep.Set(0);
                fetchedModels.Set(null);
                apiKeyError.Set(null);
                baseUrlError.Set(null);
                deepModelError.Set(null);
                balancedModelError.Set(null);
                quickModelError.Set(null);
                testSuccessMessage.Set(null);
                generalError.Set(null);
                error.Set(null);

                if (agentKey == "berget_card")
                {
                    openAiProxyBaseUrl.Set("https://api.berget.ai/v1");
                    lastDetectedProvider.Set("berget");
                    deepModel.Set("moonshotai/Kimi-K3");
                    balancedModel.Set("moonshotai/Kimi-K3");
                    quickModel.Set("moonshotai/Kimi-K3");
                }
                else if (agentKey == "anthropic_card")
                {
                    openAiProxyBaseUrl.Set("https://api.anthropic.com/v1");
                    lastDetectedProvider.Set("anthropic");
                    deepModel.Set("claude-opus-5");
                    balancedModel.Set("claude-sonnet-5");
                    quickModel.Set("claude-haiku-5");
                }
                else if (agentKey == "openaiproxy_card")
                {
                    var existingUrl = GetOpenAiProxyBaseUrlFromConfig(config);
                    if (!string.IsNullOrEmpty(existingUrl) && !existingUrl.Contains("api.berget.ai") && !existingUrl.Contains("api.anthropic.com"))
                    {
                        openAiProxyBaseUrl.Set(existingUrl);
                    }
                    else
                    {
                        openAiProxyBaseUrl.Set("https://api.openai.com");
                    }
                    var isIvyUrl = openAiProxyBaseUrl.Value.Contains("llmproxy.ivy.app");
                    lastDetectedProvider.Set(isIvyUrl ? "ivy" : "openai");
                    deepModel.Set(isIvyUrl ? "claude-opus-5" : "gpt-5.6-sol");
                    balancedModel.Set(isIvyUrl ? "gemini-3.7-flash" : "gpt-5.6-terra");
                    quickModel.Set(isIvyUrl ? "gemini-3.7-flash" : "gpt-5.6-luna");
                }
                else
                {
                    _ = RunFlowAsync(agentKey);
                }
            }, error.Value);
        }

        if (progressMessage.Value is null && (selectedAgent.Value == "openaiproxy_card" || selectedAgent.Value == "anthropic_card" || selectedAgent.Value == "berget_card"))
        {
            var isBergetCard = selectedAgent.Value == "berget_card";
            var isAnthropicCard = selectedAgent.Value == "anthropic_card";
            var isIvy = !isBergetCard && !isAnthropicCard && (openAiProxyBaseUrl.Value.Contains("llmproxy.ivy.app") || openAiProxyBaseUrl.Value.Contains("ivy.app"));

            var cardTitle = isIvy
                ? "Setup Ivy Proxy"
                : (isBergetCard
                    ? "Setup Berget AI"
                    : (isAnthropicCard
                        ? "Setup Anthropic"
                        : "Setup OpenAI"));

            var defaultUrl = isIvy
                ? "https://llmproxy.ivy.app"
                : (isBergetCard
                    ? "https://api.berget.ai/v1"
                    : (isAnthropicCard
                        ? "https://api.anthropic.com/v1"
                        : "https://api.openai.com"));

            var isGoogle = !isIvy && !isAnthropicCard && !isBergetCard && (openAiProxyBaseUrl.Value.Contains("generativelanguage.googleapis.com") || openAiProxyBaseUrl.Value.Contains("gemini") || openAiProxyBaseUrl.Value.Contains("google"));
            var currentProviderKey = isIvy ? "ivy" : (isAnthropicCard ? "anthropic" : (isBergetCard ? "berget" : (isGoogle ? "google" : "openai")));
            if (lastDetectedProvider.Value != currentProviderKey)
            {
                lastDetectedProvider.Set(currentProviderKey);
                if (isIvy)
                {
                    deepModel.Set("claude-opus-5");
                    balancedModel.Set("gemini-3.7-flash");
                    quickModel.Set("gemini-3.7-flash");
                }
                else if (isAnthropicCard)
                {
                    deepModel.Set("claude-opus-5");
                    balancedModel.Set("claude-sonnet-5");
                    quickModel.Set("claude-haiku-5");
                }
                else if (isBergetCard)
                {
                    deepModel.Set("moonshotai/Kimi-K3");
                    balancedModel.Set("moonshotai/Kimi-K3");
                    quickModel.Set("moonshotai/Kimi-K3");
                }
                else if (isGoogle)
                {
                    deepModel.Set("gemini-3.7-flash");
                    balancedModel.Set("gemini-3.7-flash");
                    quickModel.Set("gemini-3.7-flash");
                }
                else
                {
                    deepModel.Set("gpt-5.6-sol");
                    balancedModel.Set("gpt-5.6-terra");
                    quickModel.Set("gpt-5.6-luna");
                }
            }

            if (byoSubStep.Value == 0)
            {
                object agentInputs = isBergetCard
                    ? (Layout.Vertical().Width(Size.Auto().Max(Size.Units(120)))
                        | openAiProxyApiKey.ToPasswordInput("sk-...")
                            .Invalid(apiKeyError.Value)
                            .WithField()
                            .Label("API Key"))
                    : (Layout.Vertical().Width(Size.Auto().Max(Size.Units(120)))
                        | openAiProxyBaseUrl.ToTextInput(defaultUrl)
                            .Invalid(baseUrlError.Value)
                            .WithField()
                            .Label("API Base URL")
                        | openAiProxyApiKey.ToPasswordInput("sk-...")
                            .Invalid(apiKeyError.Value)
                            .WithField()
                            .Label("API Key"));

                return Layout.Vertical()
                       | Text.H3(cardTitle)
                       | (generalError.Value != null ? Callout.Error(generalError.Value) : null!)
                       | agentInputs
                       | (Layout.Horizontal()
                           | new Button("Back")
                               .Ghost()
                               .OnClick(() =>
                               {
                                   selectedAgent.Set(null);
                                   error.Set(null);
                                   generalError.Set(null);
                                   apiKeyError.Set(null);
                                   baseUrlError.Set(null);
                               })
                           | new Button("Continue")
                               .Primary()
                               .Loading(isFetchingModels.Value)
                               .OnClick(async () =>
                               {
                                   apiKeyError.Set(null);
                                   baseUrlError.Set(null);
                                   generalError.Set(null);

                                   if (string.IsNullOrWhiteSpace(openAiProxyApiKey.Value))
                                   {
                                       apiKeyError.Set("API Key is required.");
                                       return;
                                   }

                                   var baseUrl = isBergetCard || string.IsNullOrWhiteSpace(openAiProxyBaseUrl.Value)
                                       ? defaultUrl
                                       : openAiProxyBaseUrl.Value;

                                   if (!isBergetCard && string.IsNullOrWhiteSpace(baseUrl))
                                   {
                                       baseUrlError.Set("API Base URL is required.");
                                       return;
                                   }

                                   isFetchingModels.Set(true);

                                   try
                                   {
                                       var models = await OpenAiProxyModelCatalog.FetchModelsFromEndpointAsync(baseUrl, openAiProxyApiKey.Value);
                                       fetchedModels.Set(models);

                                       // Set default profile models appropriately
                                       if (isIvy)
                                       {
                                           deepModel.Set(models.Any(m => m.Id == "claude-opus-5") ? "claude-opus-5" : (models.FirstOrDefault()?.Id ?? "claude-opus-5"));
                                           balancedModel.Set(models.Any(m => m.Id == "gemini-3.7-flash") ? "gemini-3.7-flash" : (models.ElementAtOrDefault(1)?.Id ?? models.FirstOrDefault()?.Id ?? "gemini-3.7-flash"));
                                           quickModel.Set(models.Any(m => m.Id == "gemini-3.7-flash") ? "gemini-3.7-flash" : (models.ElementAtOrDefault(2)?.Id ?? models.FirstOrDefault()?.Id ?? "gemini-3.7-flash"));
                                       }
                                       else if (isAnthropicCard)
                                       {
                                           deepModel.Set(models.Any(m => m.Id == "claude-opus-5") ? "claude-opus-5" : (models.FirstOrDefault()?.Id ?? "claude-opus-5"));
                                           balancedModel.Set(models.Any(m => m.Id == "claude-sonnet-5") ? "claude-sonnet-5" : (models.ElementAtOrDefault(1)?.Id ?? models.FirstOrDefault()?.Id ?? "claude-sonnet-5"));
                                           quickModel.Set(models.Any(m => m.Id == "claude-haiku-5") ? "claude-haiku-5" : (models.ElementAtOrDefault(2)?.Id ?? models.FirstOrDefault()?.Id ?? "claude-haiku-5"));
                                       }
                                       else if (isBergetCard)
                                       {
                                           deepModel.Set("moonshotai/Kimi-K3");
                                           balancedModel.Set("moonshotai/Kimi-K3");
                                           quickModel.Set("moonshotai/Kimi-K3");
                                       }
                                       else if (isGoogle)
                                       {
                                           deepModel.Set("gemini-3.7-flash");
                                           balancedModel.Set("gemini-3.7-flash");
                                           quickModel.Set("gemini-3.7-flash");
                                       }
                                       else
                                       {
                                           deepModel.Set(models.Any(m => m.Id == "gpt-5.6-sol") ? "gpt-5.6-sol" : (models.FirstOrDefault()?.Id ?? "gpt-5.6-sol"));
                                           balancedModel.Set(models.Any(m => m.Id == "gpt-5.6-terra") ? "gpt-5.6-terra" : (models.ElementAtOrDefault(1)?.Id ?? models.FirstOrDefault()?.Id ?? "gpt-5.6-terra"));
                                           quickModel.Set(models.Any(m => m.Id == "gpt-5.6-luna") ? "gpt-5.6-luna" : (models.ElementAtOrDefault(2)?.Id ?? models.FirstOrDefault()?.Id ?? "gpt-5.6-luna"));
                                       }

                                       byoSubStep.Set(1);
                                   }
                                   catch (Exception ex)
                                   {
                                       generalError.Set($"Failed to fetch models: {ex.Message}");
                                   }
                                   finally
                                   {
                                       isFetchingModels.Set(false);
                                   }
                               })
                       );
            }

            // SubStep 1: Model Selection
            var modelsList = fetchedModels.Value ?? OpenAiProxyModelCatalog.GetModelsForBaseUrl(openAiProxyBaseUrl.Value);
            var modelOptions = modelsList
                .Select(m => new Option<string>(m.DisplayName, m.Id))
                .Concat([new Option<string>("+ Custom Model Name...", "__custom__")])
                .ToArray<IAnyOption>();

            // Ensure valid selected values
            if (deepModel.Value == "default" || (!modelsList.Any(m => m.Id.Equals(deepModel.Value, StringComparison.OrdinalIgnoreCase)) && deepModel.Value != "__custom__"))
            {
                deepModel.Set(modelsList.FirstOrDefault()?.Id ?? "__custom__");
            }
            if (balancedModel.Value == "default" || (!modelsList.Any(m => m.Id.Equals(balancedModel.Value, StringComparison.OrdinalIgnoreCase)) && balancedModel.Value != "__custom__"))
            {
                balancedModel.Set(modelsList.ElementAtOrDefault(1)?.Id ?? modelsList.FirstOrDefault()?.Id ?? "__custom__");
            }
            if (quickModel.Value == "default" || (!modelsList.Any(m => m.Id.Equals(quickModel.Value, StringComparison.OrdinalIgnoreCase)) && quickModel.Value != "__custom__"))
            {
                quickModel.Set(modelsList.ElementAtOrDefault(2)?.Id ?? modelsList.FirstOrDefault()?.Id ?? "__custom__");
            }

            object profileModels = Layout.Vertical().Width(Size.Auto().Max(Size.Units(120)))
                | Text.Block("Profile Models").Bold()
                | Text.Muted("Select models from your endpoint or type in a custom model name.").Small()
                | (Layout.Vertical()
                    | deepModel.ToSelectInput(modelOptions)
                        .Invalid(deepModelError.Value)
                        .WithField()
                        .Label("Deep Profile")
                    | (deepModel.Value == "__custom__"
                        ? customDeepText.ToTextInput("e.g. gpt-4-turbo")
                            .Invalid(deepModelError.Value)
                            .WithField()
                            .Label("Custom Deep Model Name")
                        : null))
                | (Layout.Vertical()
                    | balancedModel.ToSelectInput(modelOptions)
                        .Invalid(balancedModelError.Value)
                        .WithField()
                        .Label("Balanced Profile")
                    | (balancedModel.Value == "__custom__"
                        ? customBalancedText.ToTextInput("e.g. gpt-4-turbo")
                            .Invalid(balancedModelError.Value)
                            .WithField()
                            .Label("Custom Balanced Model Name")
                        : null))
                | (Layout.Vertical()
                    | quickModel.ToSelectInput(modelOptions)
                        .Invalid(quickModelError.Value)
                        .WithField()
                        .Label("Quick Profile")
                    | (quickModel.Value == "__custom__"
                        ? customQuickText.ToTextInput("e.g. gpt-4-mini")
                            .Invalid(quickModelError.Value)
                            .WithField()
                            .Label("Custom Quick Model Name")
                        : null));

            return Layout.Vertical()
                   | Text.H3($"{cardTitle} — Select Models")
                   | (generalError.Value != null ? Callout.Error(generalError.Value) : null!)
                   | profileModels
                   | (testSuccessMessage.Value != null
                       ? Callout.Success(testSuccessMessage.Value)
                       : null!)
                   | (Layout.Horizontal()
                       | new Button("Back")
                           .Ghost()
                           .OnClick(() =>
                           {
                               byoSubStep.Set(0);
                               deepModelError.Set(null);
                               balancedModelError.Set(null);
                               quickModelError.Set(null);
                               testSuccessMessage.Set(null);
                               generalError.Set(null);
                           })
                       | new Button("Test Endpoint")
                           .Outline()
                           .Loading(isTestingModels.Value)
                           .OnClick(async () =>
                           {
                               deepModelError.Set(null);
                               balancedModelError.Set(null);
                               quickModelError.Set(null);
                               testSuccessMessage.Set(null);
                               generalError.Set(null);

                               var dm = deepModel.Value == "__custom__"
                                   ? customDeepText.Value.Trim()
                                   : deepModel.Value;
                               var bm = balancedModel.Value == "__custom__"
                                   ? customBalancedText.Value.Trim()
                                   : balancedModel.Value;
                               var qm = quickModel.Value == "__custom__"
                                   ? customQuickText.Value.Trim()
                                   : quickModel.Value;

                               var hasValidationErr = false;
                               if (string.IsNullOrWhiteSpace(dm) || dm == "__custom__")
                               {
                                   deepModelError.Set("Please specify a valid model for Deep profile.");
                                   hasValidationErr = true;
                               }
                               if (string.IsNullOrWhiteSpace(bm) || bm == "__custom__")
                               {
                                   balancedModelError.Set("Please specify a valid model for Balanced profile.");
                                   hasValidationErr = true;
                               }
                               if (string.IsNullOrWhiteSpace(qm) || qm == "__custom__")
                               {
                                   quickModelError.Set("Please specify a valid model for Quick profile.");
                                   hasValidationErr = true;
                               }

                               if (hasValidationErr) return;

                               var baseUrl = isBergetCard || string.IsNullOrWhiteSpace(openAiProxyBaseUrl.Value)
                                   ? defaultUrl
                                   : openAiProxyBaseUrl.Value;

                               isTestingModels.Set(true);
                               try
                               {
                                   var tested = new Dictionary<string, (bool Ok, string? Err)>(StringComparer.OrdinalIgnoreCase);

                                   async Task<(bool Ok, string? Err)> TestOnceAsync(string modelId)
                                   {
                                       if (tested.TryGetValue(modelId, out var existing)) return existing;
                                       var res = await OpenAiProxyModelCatalog.TestModelEndpointAsync(baseUrl, openAiProxyApiKey.Value, modelId);
                                       tested[modelId] = res;
                                       return res;
                                   }

                                   var deepRes = await TestOnceAsync(dm);
                                   if (!deepRes.Ok) deepModelError.Set(deepRes.Err);

                                   var balancedRes = await TestOnceAsync(bm);
                                   if (!balancedRes.Ok) balancedModelError.Set(balancedRes.Err);

                                   var quickRes = await TestOnceAsync(qm);
                                   if (!quickRes.Ok) quickModelError.Set(quickRes.Err);

                                   if (deepRes.Ok && balancedRes.Ok && quickRes.Ok)
                                   {
                                       testSuccessMessage.Set("All profile models responded successfully!");
                                   }
                               }
                               catch (Exception ex)
                               {
                                   generalError.Set($"Test error: {ex.Message}");
                               }
                               finally
                               {
                                   isTestingModels.Set(false);
                               }
                           })
                       | new Button("Continue")
                           .Primary()
                           .OnClick(() =>
                           {
                               deepModelError.Set(null);
                               balancedModelError.Set(null);
                               quickModelError.Set(null);
                               generalError.Set(null);
                               testSuccessMessage.Set(null);

                               var dm = deepModel.Value == "__custom__"
                                   ? customDeepText.Value.Trim()
                                   : deepModel.Value;
                               var bm = balancedModel.Value == "__custom__"
                                   ? customBalancedText.Value.Trim()
                                   : balancedModel.Value;
                               var qm = quickModel.Value == "__custom__"
                                   ? customQuickText.Value.Trim()
                                   : quickModel.Value;

                               var hasValidationErr = false;
                               if (string.IsNullOrWhiteSpace(dm) || dm == "__custom__")
                               {
                                   deepModelError.Set("Please specify a valid model for Deep profile.");
                                   hasValidationErr = true;
                               }
                               if (string.IsNullOrWhiteSpace(bm) || bm == "__custom__")
                               {
                                   balancedModelError.Set("Please specify a valid model for Balanced profile.");
                                   hasValidationErr = true;
                               }
                               if (string.IsNullOrWhiteSpace(qm) || qm == "__custom__")
                               {
                                   quickModelError.Set("Please specify a valid model for Quick profile.");
                                   hasValidationErr = true;
                               }

                               if (hasValidationErr) return;

                               var baseUrl = isBergetCard || string.IsNullOrWhiteSpace(openAiProxyBaseUrl.Value)
                                   ? defaultUrl
                                   : openAiProxyBaseUrl.Value;

                               var targetAgent = isIvy ? "ivy" : "openaiproxy";

                               if (isIvy)
                               {
                                   SaveProfiles(config, "ivy", dm, bm, qm);
                                   SaveIvyApiKey(config, openAiProxyApiKey.Value);
                                   SaveOpenAiProxyApiKey(config, "");
                                   SaveOpenAiProxyBaseUrl(config, "");
                               }
                               else
                               {
                                   SaveProfiles(config, "openaiproxy", dm, bm, qm);
                                   SaveOpenAiProxyBaseUrl(config, baseUrl);
                                   SaveOpenAiProxyApiKey(config, openAiProxyApiKey.Value);
                                   SaveIvyApiKey(config, "");
                               }

                               config.Settings.CodingAgent = targetAgent;
                               config.SaveSettings();
                               _ = RunFlowAsync(targetAgent);
                           })
                   );
        }

        var selectedLabel = Agents.FirstOrDefault(a => a.Key == selectedAgent.Value)?.Label
            ?? (selectedAgent.Value == "berget_card" ? "Berget AI"
            : (selectedAgent.Value == "openaiproxy_card" ? "OpenAI"
            : (selectedAgent.Value == "anthropic_card" ? "Anthropic"
            : (selectedAgent.Value == "openaiproxy" ? "OpenAI Proxy" : selectedAgent.Value))));

        return Layout.Vertical()
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
        Action<string> onSelect,
        string? errorMessage)
    {
        var grid = Layout.Grid().Columns(3);

        grid = agents.Aggregate(grid, (current, a) =>
            current | new Card(
                Layout.Horizontal()
                    .AlignContent(Align.Center)
                | a.Logo.ToIcon().Width(Size.Px(32)).Height(Size.Px(32))
                | Text.Block(a.Label)
            ).OnClick(() => onSelect(a.Key)));

        var byoGrid = Layout.Grid().Columns(3);
        byoGrid = ByoAgents.Aggregate(byoGrid, (current, a) =>
            current | new Card(
                Layout.Horizontal()
                    .AlignContent(Align.Center)
                | a.Logo.ToIcon().Width(Size.Px(32)).Height(Size.Px(32))
                | Text.Block(a.Label)
            ).OnClick(() => onSelect(a.Key)));

        var byoSection = Layout.Vertical()
            | Text.Block("Bring your own LLM").Bold()
            | byoGrid;

        return Layout.Vertical()
               | Text.H3("What is your coding agent?")
               | Text.Muted(
                   "Tendril is a coding orchestrator that runs on top of your own coding agent. Pick the agent you'd like to use:")
               | (errorMessage != null ? Text.Danger(errorMessage) : null!)
               | grid
               | byoSection;
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

    private static void SaveIvyApiKey(IConfigService config, string apiKey)
    {
        var ac = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals("ivy", StringComparison.OrdinalIgnoreCase));

        if (ac == null)
        {
            ac = new AgentConfig { Name = "ivy" };
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
