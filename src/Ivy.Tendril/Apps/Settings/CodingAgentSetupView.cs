using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Net.Http;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;
using Ivy.Tendril.Agents.Providers.Ivy;
using Ivy.Tendril.Apps.Settings.Dialogs;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Settings;

public class CodingAgentSetupView : ViewBase
{
    private record AgentInfo(string Key, string Label, Icons Logo);
    private record ByoAgentInfo(string Key, string Label, Icons Logo);

    private static readonly AgentInfo[] Agents =
    [
        new("claude", "Claude", AgentBranding.IconFor("claude")),
        new("copilot", "Copilot", AgentBranding.IconFor("copilot")),
        new("codex", "Codex", AgentBranding.IconFor("codex")),
        new("gemini", "Gemini", AgentBranding.IconFor("gemini")),
        new("antigravity", "Antigravity", AgentBranding.IconFor("antigravity")),
        new("opencode", "OpenCode", AgentBranding.IconFor("opencode"))
    ];

    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();
        var runner = UseService<IAgentRunner>();

        var selectedAgent = UseState(GetInitialSelectedAgent(config));
        var openAiProxyBaseUrl = UseState(GetInitialByoUrl(config));
        var openAiProxyApiKey = UseState(GetInitialApiKey(config));

        var deepModel = UseState(GetInitialDeepModel(config, runner));
        var balancedModel = UseState(GetInitialBalancedModel(config, runner));
        var quickModel = UseState(GetInitialQuickModel(config, runner));
        var deepEffort = UseState(GetInitialDeepEffort(config));
        var balancedEffort = UseState(GetInitialBalancedEffort(config));
        var quickEffort = UseState(GetInitialQuickEffort(config));
        var useCustomModelNames = UseState(false);
        var showTestDialog = UseState(false);
        var testAgentId = UseState(GetInitialCodingAgent(config));

        var modelsQuery = UseQuery<ModelInfo[], string>(
            ResolveFinalAgent(selectedAgent.Value, openAiProxyBaseUrl.Value),
            async (agentId, ct) =>
            {
                var catalog = runner.GetModelCatalog(agentId);
                if (catalog is null) return [];
                var result = await catalog.GetModelsAsync(ct);
                return result.Models.ToArray();
            },
            initialValue: runner.GetModelCatalog(GetInitialCodingAgent(config))?.GetStaticModels()?.ToArray() ?? []
        );

        var isBerget = selectedAgent.Value == "berget_card";
        var isAnthropic = selectedAgent.Value == "anthropic_card";
        var isIvy = (selectedAgent.Value == "openaiproxy_card" || selectedAgent.Value == "ivy") &&
                    (openAiProxyBaseUrl.Value.Contains("llmproxy.ivy.app") || openAiProxyBaseUrl.Value.Contains("ivy.app"));
        var isOpenAi = selectedAgent.Value == "openaiproxy_card" && !isIvy;
        var finalAgent = ResolveFinalAgent(selectedAgent.Value, openAiProxyBaseUrl.Value);
        var rawModels = modelsQuery.Value ?? [];
        var models = ModelCatalogSorter.Sort(rawModels);
        var knownModelIds = new HashSet<string>(models.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
        var extraOptions = new List<Option<string>>();
        foreach (var m in new[] { deepModel.Value, balancedModel.Value, quickModel.Value })
        {
            if (!string.IsNullOrEmpty(m) && m != "default" && !knownModelIds.Contains(m) && extraOptions.All(o => (string?)o.Value != m))
            {
                extraOptions.Add(new Option<string>(m, m));
            }
        }

        var modelOptions = new[] { new Option<string>("Default", "default") }
            .Concat(models
                .Where(m => m.Id != "default")
                .Select(m => new Option<string>(m.DisplayName, m.Id)))
            .Concat(extraOptions)
            .ToArray<IAnyOption>();

        IAgentDescriptor? descriptor = null;
        try
        {
            descriptor = runner.GetDescriptor(finalAgent);
        }
        catch
        {
        }

        var supportsEffort = descriptor != null && descriptor.Capabilities.HasFlag(AgentCapabilities.EffortControl);

        IAnyOption[] GetEffortOptions(string? modelId)
        {
            var modelInfo = !string.IsNullOrEmpty(modelId)
                ? models.FirstOrDefault(m => m.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase))
                : null;

            var efforts = modelInfo?.SupportedEfforts;
            if (efforts == null || efforts.Count == 0)
            {
                efforts = descriptor?.GetSupportedEfforts(modelId);
            }
            if (efforts == null || efforts.Count == 0)
            {
                efforts = descriptor?.SupportedEfforts;
            }
            if (efforts == null || efforts.Count == 0)
            {
                efforts = EffortLevels.Claude;
            }

            return new[] { new Option<string>("Default", "default") }
                .Concat(efforts.Select(e => new Option<string>(e.DisplayName, e.Id)))
                .ToArray<IAnyOption>();
        }

        var hasAgentChanges = finalAgent != config.Settings.CodingAgent;
        var hasProfileChanges =
            deepModel.Value != GetProfileModel(config, finalAgent, "deep") ||
            balancedModel.Value != GetProfileModel(config, finalAgent, "balanced") ||
            quickModel.Value != GetProfileModel(config, finalAgent, "quick") ||
            (supportsEffort && (
                deepEffort.Value != GetProfileEffort(config, finalAgent, "deep") ||
                balancedEffort.Value != GetProfileEffort(config, finalAgent, "balanced") ||
                quickEffort.Value != GetProfileEffort(config, finalAgent, "quick")
            ));

        var currentIvyKey = GetIvyApiKeyFromConfig(config);
        var currentOpenAiBaseUrl = GetOpenAiProxyBaseUrlFromConfig(config);
        var currentOpenAiKey = GetOpenAiProxyApiKeyFromConfig(config);

        bool hasCredsChanged = false;
        if (isIvy)
        {
            hasCredsChanged = openAiProxyApiKey.Value != currentIvyKey || openAiProxyBaseUrl.Value != currentOpenAiBaseUrl;
        }
        else if (isBerget)
        {
            hasCredsChanged = openAiProxyApiKey.Value != currentOpenAiKey || !currentOpenAiBaseUrl.Contains("api.berget.ai");
        }
        else if (isAnthropic)
        {
            hasCredsChanged = openAiProxyApiKey.Value != currentOpenAiKey || openAiProxyBaseUrl.Value != currentOpenAiBaseUrl;
        }
        else if (isOpenAi)
        {
            hasCredsChanged = openAiProxyApiKey.Value != currentOpenAiKey || openAiProxyBaseUrl.Value != currentOpenAiBaseUrl;
        }

        var hasChanges = hasAgentChanges || hasProfileChanges || hasCredsChanged;

        var registeredAgents = runner.RegisteredAgents;
        var topAgents = Agents.Where(a => registeredAgents.Contains(a.Key)).ToArray();

        var topGrid = Layout.Grid()
            .Columns(2.At(Breakpoint.Mobile).And(Breakpoint.Desktop, 3));
        topGrid = topAgents.Aggregate(topGrid, (current, a) =>
            current | new Card(
                Layout.Horizontal()
                | a.Logo.ToIcon().Width(Size.Px(32)).Height(Size.Px(32))
                | Text.Block(a.Label)
                | new Spacer()
                | (a.Key == selectedAgent.Value ? Icons.Check.ToIcon() : null)
            ).Width(Size.Full()).Height(Size.Full()).OnClick(() =>
            {
                selectedAgent.Set(a.Key);
                testAgentId.Set(a.Key);
                var (d, b, q, de, be, qe) = ResolveAgentModelsAndEffort(config, runner, a.Key, a.Key, "");
                deepModel.Set(d);
                balancedModel.Set(b);
                quickModel.Set(q);
                deepEffort.Set(de);
                balancedEffort.Set(be);
                quickEffort.Set(qe);
            }));

        var byoAgents = new[]
        {
            new ByoAgentInfo("openaiproxy_card", "OpenAI", Icons.OpenAI),
            new ByoAgentInfo("anthropic_card", "Anthropic", Icons.ClaudeCode),
            new ByoAgentInfo("berget_card", "Berget AI", Icons.ChevronUp)
        };

        var byoGrid = Layout.Grid()
            .Columns(2.At(Breakpoint.Mobile).And(Breakpoint.Desktop, 3));
        byoGrid = byoAgents.Aggregate(byoGrid, (current, a) =>
            current | new Card(
                Layout.Horizontal()
                | a.Logo.ToIcon().Width(Size.Px(32)).Height(Size.Px(32))
                | Text.Block(a.Label)
                | new Spacer()
                | (a.Key == selectedAgent.Value ? Icons.Check.ToIcon() : null)
            ).Width(Size.Full()).Height(Size.Full()).OnClick(() =>
            {
                selectedAgent.Set(a.Key);
                string newUrl = openAiProxyBaseUrl.Value;
                if (a.Key == "openaiproxy_card")
                {
                    if (newUrl.Contains("api.anthropic.com") || newUrl.Contains("api.berget.ai"))
                    {
                        newUrl = "https://api.openai.com";
                        openAiProxyBaseUrl.Set(newUrl);
                    }
                }
                else if (a.Key == "anthropic_card")
                {
                    if (string.IsNullOrEmpty(newUrl) || newUrl.Contains("api.openai.com") || newUrl.Contains("api.berget.ai") || newUrl.Contains("llmproxy.ivy.app"))
                    {
                        newUrl = "https://api.anthropic.com/v1";
                        openAiProxyBaseUrl.Set(newUrl);
                    }
                }
                else if (a.Key == "berget_card")
                {
                    if (string.IsNullOrEmpty(newUrl) || newUrl.Contains("api.openai.com") || newUrl.Contains("api.anthropic.com") || newUrl.Contains("llmproxy.ivy.app"))
                    {
                        newUrl = "https://api.berget.ai/v1";
                        openAiProxyBaseUrl.Set(newUrl);
                    }
                }

                var byoAgentId = a.Key == "openaiproxy_card" && newUrl.Contains("llmproxy.ivy.app") ? "ivy" : "openaiproxy";
                testAgentId.Set(byoAgentId);
                var (d, b, q, de, be, qe) = ResolveAgentModelsAndEffort(config, runner, byoAgentId, a.Key, newUrl);
                deepModel.Set(d);
                balancedModel.Set(b);
                quickModel.Set(q);
                deepEffort.Set(de);
                balancedEffort.Set(be);
                quickEffort.Set(qe);
            }));

        object? agentInputs = null;
        if (selectedAgent.Value == "openaiproxy_card")
        {
            agentInputs = Layout.Vertical().Width(Size.Auto().Max(Size.Units(120)))
                | openAiProxyBaseUrl.ToTextInput("https://api.openai.com")
                    .WithField()
                    .Label("API Base URL")
                | openAiProxyApiKey.ToPasswordInput("sk-...")
                    .WithField()
                    .Label("API Key");
        }
        else if (selectedAgent.Value == "anthropic_card")
        {
            agentInputs = Layout.Vertical().Width(Size.Auto().Max(Size.Units(120)))
                | openAiProxyBaseUrl.ToTextInput("https://api.anthropic.com/v1")
                    .WithField()
                    .Label("API Base URL")
                | openAiProxyApiKey.ToPasswordInput("sk-...")
                    .WithField()
                    .Label("API Key");
        }
        else if (selectedAgent.Value == "berget_card")
        {
            agentInputs = Layout.Vertical().Width(Size.Auto().Max(Size.Units(120)))
                | openAiProxyApiKey.ToPasswordInput("sk-...")
                    .WithField()
                    .Label("API Key");
        }

        var isByo = isIvy || isBerget || isAnthropic || isOpenAi;
        var hasFetchedModels = models.Count > 0;
        var isCustomMode = isByo && (useCustomModelNames.Value || !hasFetchedModels);

        var profileModels = Layout.Vertical().Width(Size.Auto().Max(Size.Units(120)))
            | (isByo && hasFetchedModels ? useCustomModelNames.ToSwitchInput(label: "Custom model names") : null!)
            | Text.Block("Profile Models").Bold()
            | Text.Muted(isCustomMode
                ? "Specify custom model names and effort level to use for each profile."
                : "Promptwares are configured to use different profiles depending on the complexity of the task. You can specify what model and effort level to use for each profile.").Small()
            | (supportsEffort
                ? (object)(Layout.Vertical()
                    | (Layout.Horizontal()
                        | (isCustomMode
                            ? deepModel.ToTextInput("e.g. gpt-4o").WithField().Label("Deep").Width(Size.Fraction(0.65f))
                            : deepModel.ToSelectInput(modelOptions).Loading(modelsQuery.Loading).WithField().Label("Deep").Width(Size.Fraction(0.65f)))
                        | deepEffort.ToSelectInput(GetEffortOptions(deepModel.Value)).WithField().Label("Effort").Width(Size.Fraction(0.35f)))
                    | (Layout.Horizontal()
                        | (isCustomMode
                            ? balancedModel.ToTextInput("e.g. gpt-4o-mini").WithField().Label("Balanced").Width(Size.Fraction(0.65f))
                            : balancedModel.ToSelectInput(modelOptions).Loading(modelsQuery.Loading).WithField().Label("Balanced").Width(Size.Fraction(0.65f)))
                        | balancedEffort.ToSelectInput(GetEffortOptions(balancedModel.Value)).WithField().Label("Effort").Width(Size.Fraction(0.35f)))
                    | (Layout.Horizontal()
                        | (isCustomMode
                            ? quickModel.ToTextInput("e.g. gpt-4o-mini").WithField().Label("Quick").Width(Size.Fraction(0.65f))
                            : quickModel.ToSelectInput(modelOptions).Loading(modelsQuery.Loading).WithField().Label("Quick").Width(Size.Fraction(0.65f)))
                        | quickEffort.ToSelectInput(GetEffortOptions(quickModel.Value)).WithField().Label("Effort").Width(Size.Fraction(0.35f))))
                : (Layout.Vertical()
                    | (isCustomMode
                        ? deepModel.ToTextInput("e.g. gpt-4o").WithField().Label("Deep")
                        : deepModel.ToSelectInput(modelOptions).Loading(modelsQuery.Loading).WithField().Label("Deep"))
                    | (isCustomMode
                        ? balancedModel.ToTextInput("e.g. gpt-4o-mini").WithField().Label("Balanced")
                        : balancedModel.ToSelectInput(modelOptions).Loading(modelsQuery.Loading).WithField().Label("Balanced"))
                    | (isCustomMode
                        ? quickModel.ToTextInput("e.g. gpt-4o-mini").WithField().Label("Quick")
                        : quickModel.ToSelectInput(modelOptions).Loading(modelsQuery.Loading).WithField().Label("Quick"))));

        return Layout.Vertical()
               | Text.Block("Coding Agent").Bold()
               | Text.Muted("Tendril connects to your configured AI coding agent or bundled open source engines like OpenCode.").Small()
               | (Layout.Vertical()
                   .Width(Size.Full().At(Breakpoint.Mobile).And(Breakpoint.Desktop, Size.Units(170)))
                   | topGrid.Width(Size.Full()))
               | (Layout.Vertical()
                   | Text.Block("Bring your own LLM").Bold()
                   | (Layout.Vertical()
                       .Width(Size.Full().At(Breakpoint.Mobile).And(Breakpoint.Desktop, Size.Units(170)))
                       | byoGrid.Width(Size.Full())))
               | agentInputs
               | profileModels
               | new Spacer()
               | (Layout.Horizontal()
                   | new Button("Test Agent").Outline()
                       .Disabled(modelsQuery.Loading)
                       .OnClick(() => showTestDialog.Set(true))
                   | new Button("Save").Primary()
                       .Disabled(!hasChanges)
                       .OnClick(() =>
                       {
                           config.Settings.CodingAgent = finalAgent;
                           SaveProfiles(config, finalAgent,
                               deepModel.Value, deepEffort.Value,
                               balancedModel.Value, balancedEffort.Value,
                               quickModel.Value, quickEffort.Value);
                           if (isIvy)
                           {
                               SaveIvyApiKey(config, openAiProxyApiKey.Value);
                               SaveIvyBaseUrl(config, openAiProxyBaseUrl.Value);
                               SaveOpenAiProxyApiKey(config, openAiProxyApiKey.Value);
                               SaveOpenAiProxyBaseUrl(config, openAiProxyBaseUrl.Value);
                           }
                           else if (isBerget)
                           {
                               var baseUrl = string.IsNullOrWhiteSpace(openAiProxyBaseUrl.Value) || !openAiProxyBaseUrl.Value.Contains("api.berget.ai")
                                   ? "https://api.berget.ai/v1"
                                   : openAiProxyBaseUrl.Value;
                               SaveOpenAiProxyBaseUrl(config, baseUrl);
                               SaveOpenAiProxyApiKey(config, openAiProxyApiKey.Value);
                           }
                           else if (isAnthropic)
                           {
                               var baseUrl = string.IsNullOrWhiteSpace(openAiProxyBaseUrl.Value) ? "https://api.anthropic.com/v1" : openAiProxyBaseUrl.Value;
                               SaveOpenAiProxyBaseUrl(config, baseUrl);
                               SaveOpenAiProxyApiKey(config, openAiProxyApiKey.Value);
                           }
                           else if (isOpenAi)
                           {
                               SaveOpenAiProxyBaseUrl(config, openAiProxyBaseUrl.Value);
                               SaveOpenAiProxyApiKey(config, openAiProxyApiKey.Value);
                           }
                           config.SaveSettings();
                           client.Toast("Coding agent settings saved", "Saved");
                       }))
               | new AgentTestDialog(
                   showTestDialog,
                   testAgentId,
                   () =>
                   {
                       var currentModels = new[] { deepModel.Value, balancedModel.Value, quickModel.Value };
                       var entries = new List<TestModelEntry>();
                       var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                       foreach (var m in currentModels)
                       {
                           if (string.IsNullOrEmpty(m) || m.Equals("default", StringComparison.OrdinalIgnoreCase))
                           {
                               if (seen.Add("default"))
                                   entries.Add(new TestModelEntry(null, "Default"));
                           }
                           else
                           {
                               if (seen.Add(m))
                               {
                                   var displayName = models.FirstOrDefault(x => x.Id.Equals(m, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? m;
                                   entries.Add(new TestModelEntry(m, displayName));
                               }
                           }
                       }

                       return entries;
                   },
                   runner);
    }

    private static string GetInitialCodingAgent(IConfigService config) =>
        string.IsNullOrWhiteSpace(config.Settings.CodingAgent) ? "claude" : config.Settings.CodingAgent;

    private static string GetInitialByoUrl(IConfigService config)
    {
        var agent = GetInitialCodingAgent(config);
        if (agent == "ivy")
        {
            var url = GetIvyBaseUrlFromConfig(config);
            return string.IsNullOrEmpty(url) ? "https://llmproxy.ivy.app" : url;
        }
        if (agent == "openaiproxy")
        {
            var url = GetOpenAiProxyBaseUrlFromConfig(config);
            return string.IsNullOrEmpty(url) ? "https://api.openai.com" : url;
        }
        return "https://api.openai.com";
    }

    private static string GetInitialApiKey(IConfigService config)
    {
        var agent = GetInitialCodingAgent(config);
        if (agent == "ivy") return GetIvyApiKeyFromConfig(config);
        if (agent == "openaiproxy") return GetOpenAiProxyApiKeyFromConfig(config);
        return "";
    }

    private static string GetInitialSelectedAgent(IConfigService config)
    {
        var agent = GetInitialCodingAgent(config);
        if (agent == "ivy") return "openaiproxy_card";
        if (agent == "openaiproxy")
        {
            var url = GetInitialByoUrl(config);
            if (url.Contains("api.berget.ai")) return "berget_card";
            if (url.Contains("api.anthropic.com")) return "anthropic_card";
            return "openaiproxy_card";
        }
        return agent;
    }

    private static string GetInitialDeepModel(IConfigService config, IAgentRunner runner) =>
        ResolveAgentModelsAndEffort(config, runner, GetInitialCodingAgent(config), GetInitialSelectedAgent(config), GetInitialByoUrl(config)).deep;

    private static string GetInitialBalancedModel(IConfigService config, IAgentRunner runner) =>
        ResolveAgentModelsAndEffort(config, runner, GetInitialCodingAgent(config), GetInitialSelectedAgent(config), GetInitialByoUrl(config)).balanced;

    private static string GetInitialQuickModel(IConfigService config, IAgentRunner runner) =>
        ResolveAgentModelsAndEffort(config, runner, GetInitialCodingAgent(config), GetInitialSelectedAgent(config), GetInitialByoUrl(config)).quick;

    private static string GetInitialDeepEffort(IConfigService config) =>
        GetProfileEffort(config, GetInitialCodingAgent(config), "deep");

    private static string GetInitialBalancedEffort(IConfigService config) =>
        GetProfileEffort(config, GetInitialCodingAgent(config), "balanced");

    private static string GetInitialQuickEffort(IConfigService config) =>
        GetProfileEffort(config, GetInitialCodingAgent(config), "quick");

    private static string ResolveFinalAgent(string selectedAgent, string baseUrl)
    {
        var isIvy = (selectedAgent == "openaiproxy_card" || selectedAgent == "ivy") &&
                    (baseUrl.Contains("llmproxy.ivy.app") || baseUrl.Contains("ivy.app"));
        var isBerget = selectedAgent == "berget_card";
        var isAnthropic = selectedAgent == "anthropic_card";
        var isOpenAi = selectedAgent == "openaiproxy_card" && !isIvy;

        if (isIvy) return "ivy";
        if (isBerget || isAnthropic || isOpenAi) return "openaiproxy";
        return selectedAgent;
    }

    private static (string deep, string balanced, string quick, string deepEff, string balancedEff, string quickEff)
        ResolveAgentModelsAndEffort(IConfigService config, IAgentRunner runner, string agentId, string cardKey, string baseUrl)
    {
        var deep = GetProfileModel(config, agentId, "deep");
        var balanced = GetProfileModel(config, agentId, "balanced");
        var quick = GetProfileModel(config, agentId, "quick");
        var deepEff = GetProfileEffort(config, agentId, "deep");
        var balancedEff = GetProfileEffort(config, agentId, "balanced");
        var quickEff = GetProfileEffort(config, agentId, "quick");

        if (deep == "default" || balanced == "default" || quick == "default")
        {
            var (isIvyAgent, isAnthropicAgent, isBergetAgent, isGoogleAgent, isOpenAiAgent) =
                DetectAgentProvider(cardKey, baseUrl);

            var catalogModels = runner.GetModelCatalog(agentId)?.GetStaticModels();

            var (defDeep, defBalanced, defQuick) = ModelProfileSelector.SelectDefaults(
                catalogModels,
                isIvy: isIvyAgent,
                isAnthropic: isAnthropicAgent,
                isBerget: isBergetAgent,
                isGoogle: isGoogleAgent,
                isOpenAi: isOpenAiAgent);

            if (deep == "default") deep = defDeep;
            if (balanced == "default") balanced = defBalanced;
            if (quick == "default") quick = defQuick;
        }

        return (deep, balanced, quick, deepEff, balancedEff, quickEff);
    }

    private static string GetProfileModel(IConfigService config, string agentId, string profileName)
    {
        var ac = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals(agentId, StringComparison.OrdinalIgnoreCase));
        var profile = ac?.Profiles.FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
        var value = profile?.Model;
        return string.IsNullOrEmpty(value) ? "default" : value;
    }

    private static string GetProfileEffort(IConfigService config, string agentId, string profileName)
    {
        var ac = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals(agentId, StringComparison.OrdinalIgnoreCase));
        var profile = ac?.Profiles.FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
        var value = profile?.Effort;
        return string.IsNullOrWhiteSpace(value) ? "default" : value.ToLowerInvariant();
    }

    private static void SaveProfiles(IConfigService config, string agentId,
        string deepModel, string deepEffort,
        string balancedModel, string balancedEffort,
        string quickModel, string quickEffort)
    {
        var ac = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals(agentId, StringComparison.OrdinalIgnoreCase));

        if (ac == null)
        {
            ac = new AgentConfig { Name = agentId };
            config.Settings.CodingAgents.Add(ac);
        }

        SetProfile(ac, "deep", deepModel, deepEffort);
        SetProfile(ac, "balanced", balancedModel, balancedEffort);
        SetProfile(ac, "quick", quickModel, quickEffort);
    }

    private static void SetProfile(AgentConfig ac, string profileName, string model, string effort)
    {
        var profile = ac.Profiles.FirstOrDefault(p =>
            p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));

        if (profile == null)
        {
            profile = new AgentProfileConfig { Name = profileName };
            ac.Profiles.Add(profile);
        }

        profile.Model = model;
        profile.Effort = string.IsNullOrWhiteSpace(effort) || effort.Equals("default", StringComparison.OrdinalIgnoreCase) ? "" : effort;
    }

    private static string GetIvyApiKeyFromConfig(IConfigService config)
    {
        var ac = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals("ivy", StringComparison.OrdinalIgnoreCase));
        if (ac != null && ac.EnvironmentVariables.TryGetValue("ANTHROPIC_API_KEY", out var key))
            return key;
        return "";
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
            ac.EnvironmentVariables.Remove("OPENAI_API_KEY");
            ac.EnvironmentVariables.Remove("IVY_API_KEY");
        }
        else
        {
            ac.EnvironmentVariables["ANTHROPIC_API_KEY"] = apiKey;
            ac.EnvironmentVariables["OPENAI_API_KEY"] = apiKey;
            ac.EnvironmentVariables["IVY_API_KEY"] = apiKey;
        }
    }

    private static string GetIvyBaseUrlFromConfig(IConfigService config)
    {
        var ac = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals("ivy", StringComparison.OrdinalIgnoreCase));
        if (ac != null)
        {
            if (ac.EnvironmentVariables.TryGetValue("ANTHROPIC_BASE_URL", out var url) && !string.IsNullOrEmpty(url)) return url;
            if (ac.EnvironmentVariables.TryGetValue("OPENAI_BASE_URL", out url) && !string.IsNullOrEmpty(url)) return url;
            if (ac.EnvironmentVariables.TryGetValue("IVY_BASE_URL", out url) && !string.IsNullOrEmpty(url)) return url;
        }
        return "";
    }

    private static void SaveIvyBaseUrl(IConfigService config, string url)
    {
        var ac = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals("ivy", StringComparison.OrdinalIgnoreCase));

        if (ac == null)
        {
            ac = new AgentConfig { Name = "ivy" };
            config.Settings.CodingAgents.Add(ac);
        }

        if (string.IsNullOrEmpty(url))
        {
            ac.EnvironmentVariables.Remove("ANTHROPIC_BASE_URL");
            ac.EnvironmentVariables.Remove("OPENAI_BASE_URL");
            ac.EnvironmentVariables.Remove("IVY_BASE_URL");
        }
        else
        {
            var trimmedBase = url.Trim().TrimEnd('/');
            ac.EnvironmentVariables["ANTHROPIC_BASE_URL"] = trimmedBase.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? trimmedBase[..^3] : trimmedBase;
            ac.EnvironmentVariables["OPENAI_BASE_URL"] = trimmedBase.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? trimmedBase : $"{trimmedBase}/v1";
            ac.EnvironmentVariables["IVY_BASE_URL"] = trimmedBase;
        }
    }

    private static string GetOpenAiProxyApiKeyFromConfig(IConfigService config)
    {
        var ac = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals("openaiproxy", StringComparison.OrdinalIgnoreCase));
        if (ac != null)
        {
            if (ac.EnvironmentVariables.TryGetValue("ANTHROPIC_API_KEY", out var key) && !string.IsNullOrEmpty(key)) return key;
            if (ac.EnvironmentVariables.TryGetValue("OPENAI_API_KEY", out key) && !string.IsNullOrEmpty(key)) return key;
        }
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
            ac.EnvironmentVariables.Remove("OPENAI_API_KEY");
        }
        else
        {
            ac.EnvironmentVariables["ANTHROPIC_API_KEY"] = apiKey;
            ac.EnvironmentVariables["OPENAI_API_KEY"] = apiKey;
        }
    }

    private static string GetOpenAiProxyBaseUrlFromConfig(IConfigService config)
    {
        var ac = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals("openaiproxy", StringComparison.OrdinalIgnoreCase));
        if (ac != null)
        {
            if (ac.EnvironmentVariables.TryGetValue("ANTHROPIC_BASE_URL", out var url) && !string.IsNullOrEmpty(url)) return url;
            if (ac.EnvironmentVariables.TryGetValue("OPENAI_BASE_URL", out url) && !string.IsNullOrEmpty(url)) return url;
        }
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
            ac.EnvironmentVariables.Remove("OPENAI_BASE_URL");
        }
        else
        {
            var trimmedBase = url.Trim().TrimEnd('/');
            ac.EnvironmentVariables["ANTHROPIC_BASE_URL"] = trimmedBase.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? trimmedBase[..^3] : trimmedBase;
            ac.EnvironmentVariables["OPENAI_BASE_URL"] = trimmedBase.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? trimmedBase : $"{trimmedBase}/v1";
        }
    }

    private static (bool IsIvy, bool IsAnthropic, bool IsBerget, bool IsGoogle, bool IsOpenAi) DetectAgentProvider(
        string agentId,
        string baseUrl)
    {
        var id = (agentId ?? "").ToLowerInvariant();
        var url = (baseUrl ?? "").ToLowerInvariant();

        if (id == "ivy" || url.Contains("llmproxy.ivy.app") || url.Contains("ivy.app"))
            return (true, false, false, false, false);

        if (id == "berget_card" || id == "berget" || url.Contains("api.berget.ai"))
            return (false, false, true, false, false);

        if (id == "anthropic_card" || id == "claude" || url.Contains("api.anthropic.com"))
            return (false, true, false, false, false);

        if (id == "gemini" || id == "antigravity" || url.Contains("generativelanguage.googleapis.com") || url.Contains("gemini") || url.Contains("google"))
            return (false, false, false, true, false);

        return (false, false, false, false, true);
    }
}
