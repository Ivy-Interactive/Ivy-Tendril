using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Net.Http;
using Ivy.Tendril.Agents.Abstractions;
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

        var selectedAgent = UseState(
            string.IsNullOrWhiteSpace(config.Settings.CodingAgent)
                ? "claude"
                : (config.Settings.CodingAgent == "ivy"
                    ? "openaiproxy_card"
                    : (config.Settings.CodingAgent == "openaiproxy"
                        ? (GetOpenAiProxyBaseUrlFromConfig(config).Contains("api.berget.ai")
                            ? "berget_card"
                            : (GetOpenAiProxyBaseUrlFromConfig(config).Contains("api.anthropic.com")
                                ? "anthropic_card"
                                : "openaiproxy_card"))
                        : config.Settings.CodingAgent))
        );

        var openAiProxyBaseUrl = UseState(
            config.Settings.CodingAgent == "ivy"
                ? "https://llmproxy.ivy.app"
                : (config.Settings.CodingAgent == "openaiproxy"
                    ? GetOpenAiProxyBaseUrlFromConfig(config)
                    : "https://api.openai.com")
        );

        var openAiProxyApiKey = UseState(
            config.Settings.CodingAgent == "ivy"
                ? GetIvyApiKeyFromConfig(config)
                : (config.Settings.CodingAgent == "openaiproxy"
                    ? GetOpenAiProxyApiKeyFromConfig(config)
                    : "")
        );

        var ollamaUrl = UseState(GetOllamaUrlFromConfig(config, config.Settings.CodingAgent == "ivy" || config.Settings.CodingAgent == "openaiproxy" ? "" : config.Settings.CodingAgent));

        var deepModel = UseState(GetProfileModel(config, config.Settings.CodingAgent, "deep"));
        var balancedModel = UseState(GetProfileModel(config, config.Settings.CodingAgent, "balanced"));
        var quickModel = UseState(GetProfileModel(config, config.Settings.CodingAgent, "quick"));
        var lastRealAgent = UseState(config.Settings.CodingAgent);
        var showTestDialog = UseState(false);
        var testAgentId = UseState(config.Settings.CodingAgent);

        var modelsQuery = UseQuery<ModelInfo[], string>(
            selectedAgent.Value == "openaiproxy_card"
                ? (openAiProxyBaseUrl.Value.Contains("llmproxy.ivy.app") ? "ivy" : "openaiproxy")
                : (selectedAgent.Value == "anthropic_card" || selectedAgent.Value == "berget_card" ? "openaiproxy" : selectedAgent.Value),
            async (agentId, ct) =>
            {
                var catalog = runner.GetModelCatalog(agentId);
                if (catalog is null) return [];
                var result = await catalog.GetModelsAsync(ct);
                return result.Models.ToArray();
            },
            initialValue: []
        );

        var isIvyInstalled = UseState(false);
        var isInstalling = UseState(false);
        var installError = UseState<string?>(null);

        var checkIvyInstall = async () =>
        {
            var hc = runner.GetHealthCheck("ivy") ?? runner.GetHealthCheck("openaiproxy");
            if (hc != null)
            {
                var status = await hc.CheckInstallAsync();
                isIvyInstalled.Set(status.IsInstalled);
            }
            else
            {
                isIvyInstalled.Set(false);
            }
        };

        UseEffect(async () =>
        {
            await checkIvyInstall();
        }, selectedAgent);

        var isBerget = selectedAgent.Value == "berget_card";
        var realAgentId = selectedAgent.Value == "openaiproxy_card"
            ? (openAiProxyBaseUrl.Value.Contains("llmproxy.ivy.app") ? "ivy" : "openaiproxy")
            : (selectedAgent.Value == "anthropic_card" || isBerget ? "openaiproxy" : selectedAgent.Value);

        if (lastRealAgent.Value != realAgentId || (isBerget && deepModel.Value == "default"))
        {
            var deep = GetProfileModel(config, realAgentId, "deep");
            var balanced = GetProfileModel(config, realAgentId, "balanced");
            var quick = GetProfileModel(config, realAgentId, "quick");

            deepModel.Set(deep == "default" && isBerget ? "kimi-k3" : deep);
            balancedModel.Set(balanced == "default" && isBerget ? "kimi-k3" : balanced);
            quickModel.Set(quick == "default" && isBerget ? "kimi-k3" : quick);
            ollamaUrl.Set(GetOllamaUrlFromConfig(config, realAgentId));
            lastRealAgent.Set(realAgentId);
            testAgentId.Set(realAgentId);
        }

        var models = modelsQuery.Value ?? [];
        var modelOptions = new[] { new Option<string>("Default", "default") }
            .Concat(models
                .Where(m => m.Id != "default")
                .Select(m => new Option<string>(m.DisplayName, m.Id)))
            .ToArray<IAnyOption>();

        var isIvy = selectedAgent.Value == "openaiproxy_card" && openAiProxyBaseUrl.Value.Contains("llmproxy.ivy.app");
        var isAnthropic = selectedAgent.Value == "anthropic_card";
        var isOpenAi = selectedAgent.Value == "openaiproxy_card" && !isIvy;

        string finalAgent;
        if (isIvy) finalAgent = "ivy";
        else if (isBerget || isAnthropic || isOpenAi) finalAgent = "openaiproxy";
        else finalAgent = selectedAgent.Value;

        var hasAgentChanges = finalAgent != config.Settings.CodingAgent;
        var hasProfileChanges =
            deepModel.Value != GetProfileModel(config, finalAgent, "deep") ||
            balancedModel.Value != GetProfileModel(config, finalAgent, "balanced") ||
            quickModel.Value != GetProfileModel(config, finalAgent, "quick");
        
        var currentIvyKey = GetIvyApiKeyFromConfig(config);
        var currentOpenAiBaseUrl = GetOpenAiProxyBaseUrlFromConfig(config);
        var currentOpenAiKey = GetOpenAiProxyApiKeyFromConfig(config);

        bool hasCredsChanged = false;
        if (isIvy)
        {
            hasCredsChanged = openAiProxyApiKey.Value != currentIvyKey;
        }
        else if (isBerget)
        {
            hasCredsChanged = openAiProxyApiKey.Value != currentOpenAiKey || !currentOpenAiBaseUrl.Contains("api.berget.ai");
        }
        else if (isAnthropic)
        {
            hasCredsChanged = openAiProxyApiKey.Value != currentOpenAiKey || !currentOpenAiBaseUrl.Contains("api.anthropic.com");
        }
        else if (isOpenAi)
        {
            hasCredsChanged = openAiProxyApiKey.Value != currentOpenAiKey || openAiProxyBaseUrl.Value != currentOpenAiBaseUrl;
        }
        else if (selectedAgent.Value == "opencode")
        {
            hasCredsChanged = ollamaUrl.Value != GetOllamaUrlFromConfig(config, selectedAgent.Value);
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
            }));

        var hasByoSupport = registeredAgents.Contains("openaiproxy") || registeredAgents.Contains("ivy");

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
                if (a.Key == "openaiproxy_card" && (openAiProxyBaseUrl.Value.Contains("api.anthropic.com") || openAiProxyBaseUrl.Value.Contains("api.berget.ai")))
                {
                    openAiProxyBaseUrl.Set("https://api.openai.com");
                }
                else if (a.Key == "anthropic_card" && (string.IsNullOrEmpty(openAiProxyBaseUrl.Value) || openAiProxyBaseUrl.Value.Contains("api.openai.com") || openAiProxyBaseUrl.Value.Contains("api.berget.ai")))
                {
                    openAiProxyBaseUrl.Set("https://api.anthropic.com/v1");
                }
                else if (a.Key == "berget_card")
                {
                    if (string.IsNullOrEmpty(openAiProxyBaseUrl.Value) || openAiProxyBaseUrl.Value.Contains("api.openai.com") || openAiProxyBaseUrl.Value.Contains("api.anthropic.com"))
                    {
                        openAiProxyBaseUrl.Set("https://api.berget.ai/v1");
                    }
                    if (deepModel.Value == "default") deepModel.Set("kimi-k3");
                    if (balancedModel.Value == "default") balancedModel.Set("kimi-k3");
                    if (quickModel.Value == "default") quickModel.Set("kimi-k3");
                }
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
                | openAiProxyApiKey.ToPasswordInput("...")
                    .WithField()
                    .Label("API Key");
        }
        else if (selectedAgent.Value == "opencode")
        {
            agentInputs = Layout.Vertical().Width(Size.Auto().Max(Size.Units(120)))
                | ollamaUrl.ToTextInput("http://localhost:11434")
                    .WithField()
                    .Label("Ollama Host");
        }

        var profileModels = Layout.Vertical().Width(Size.Auto().Max(Size.Units(120)))
            | Text.Block("Profile Models").Bold()
            | Text.Muted("Promptwares are configured to use different profiles depending on the complexity of the task. You can specify what model to use for each profile.").Small()
            | deepModel.ToSelectInput(modelOptions).Loading(modelsQuery.Loading).WithField().Label("Deep")
            | balancedModel.ToSelectInput(modelOptions).Loading(modelsQuery.Loading).WithField().Label("Balanced")
            | quickModel.ToSelectInput(modelOptions).Loading(modelsQuery.Loading).WithField().Label("Quick");

        return Layout.Vertical()
               | Text.Block("Coding Agent").Bold()
               | (Layout.Vertical()
                   .Width(Size.Full().At(Breakpoint.Mobile).And(Breakpoint.Desktop, Size.Units(170)))
                   | topGrid.Width(Size.Full()))
               | (hasByoSupport
                   ? (object)(Layout.Vertical()
                       | Text.Block("Bring your own LLM").Bold()
                       | (Layout.Vertical()
                           .Width(Size.Full().At(Breakpoint.Mobile).And(Breakpoint.Desktop, Size.Units(170)))
                           | byoGrid.Width(Size.Full())))
                   : null!)
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
                           SaveProfiles(config, finalAgent, deepModel.Value, balancedModel.Value, quickModel.Value);
                           if (isIvy)
                           {
                               SaveIvyApiKey(config, openAiProxyApiKey.Value);
                               SaveOpenAiProxyApiKey(config, "");
                               SaveOpenAiProxyBaseUrl(config, "");
                           }
                           else if (isBerget)
                           {
                               var baseUrl = string.IsNullOrWhiteSpace(openAiProxyBaseUrl.Value) || !openAiProxyBaseUrl.Value.Contains("api.berget.ai")
                                   ? "https://api.berget.ai/v1"
                                   : openAiProxyBaseUrl.Value;
                               SaveOpenAiProxyBaseUrl(config, baseUrl);
                               SaveOpenAiProxyApiKey(config, openAiProxyApiKey.Value);
                               SaveIvyApiKey(config, "");
                           }
                           else if (isAnthropic)
                           {
                               var baseUrl = string.IsNullOrWhiteSpace(openAiProxyBaseUrl.Value) ? "https://api.anthropic.com/v1" : openAiProxyBaseUrl.Value;
                               SaveOpenAiProxyBaseUrl(config, baseUrl);
                               SaveOpenAiProxyApiKey(config, openAiProxyApiKey.Value);
                               SaveIvyApiKey(config, "");
                           }
                           else if (isOpenAi)
                           {
                               SaveOpenAiProxyBaseUrl(config, openAiProxyBaseUrl.Value);
                               SaveOpenAiProxyApiKey(config, openAiProxyApiKey.Value);
                               SaveIvyApiKey(config, "");
                           }
                           else
                           {
                               SaveOllamaUrl(config, selectedAgent.Value, ollamaUrl.Value);
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

    private static string GetProfileModel(IConfigService config, string agentId, string profileName)
    {
        var ac = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals(agentId, StringComparison.OrdinalIgnoreCase));
        var profile = ac?.Profiles.FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
        var value = profile?.Model;
        return string.IsNullOrEmpty(value) ? "default" : value;
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
        }
        else
        {
            ac.EnvironmentVariables["ANTHROPIC_API_KEY"] = apiKey;
        }
    }

    private static string GetIvyBaseUrlFromConfig(IConfigService config)
    {
        var ac = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals("ivy", StringComparison.OrdinalIgnoreCase));
        if (ac != null && ac.EnvironmentVariables.TryGetValue("ANTHROPIC_BASE_URL", out var url))
            return url;
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
        }
        else
        {
            ac.EnvironmentVariables["ANTHROPIC_BASE_URL"] = url;
        }
    }

    private static string GetOllamaUrlFromConfig(IConfigService config, string agentId)
    {
        var ac = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals(agentId, StringComparison.OrdinalIgnoreCase));
        if (ac != null)
        {
            if (ac.EnvironmentVariables.TryGetValue("OLLAMA_HOST", out var host) && !string.IsNullOrEmpty(host))
                return host;
            if (ac.EnvironmentVariables.TryGetValue("OLLAMA_BASE_URL", out var baseUrl) && !string.IsNullOrEmpty(baseUrl))
                return baseUrl;
        }
        return "";
    }

    private static void SaveOllamaUrl(IConfigService config, string agentId, string url)
    {
        var ac = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals(agentId, StringComparison.OrdinalIgnoreCase));

        if (ac == null)
        {
            if (string.IsNullOrEmpty(url)) return;
            ac = new AgentConfig { Name = agentId };
            config.Settings.CodingAgents.Add(ac);
        }

        if (string.IsNullOrEmpty(url))
        {
            ac.EnvironmentVariables.Remove("OLLAMA_HOST");
            ac.EnvironmentVariables.Remove("OLLAMA_BASE_URL");
        }
        else
        {
            ac.EnvironmentVariables["OLLAMA_HOST"] = url;
            ac.EnvironmentVariables["OLLAMA_BASE_URL"] = url;
        }
    }


    private static async Task<bool> InstallIvyAgentAsync(IClientProvider client)
    {
        string os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "darwin" : "linux";
        string arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var installDir = Path.Combine(home, ".ivy-agent", "bin");
        Directory.CreateDirectory(installDir);
        
        var tempDir = Path.Combine(Path.GetTempPath(), "ivy-agent-install");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        Directory.CreateDirectory(tempDir);
        
        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("IvyTendril");
            
            // 1. Get latest version from CDN
            string latestTxtUrl = "https://cdn.ivy.app/ivy-agent-cli/releases/latest.txt";
            string version;
            try
            {
                version = (await httpClient.GetStringAsync(latestTxtUrl)).Trim();
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to fetch latest version metadata from CDN: {ex.Message}");
            }
            
            // 2. Download the archive
            string extension = os == "windows" ? ".zip" : ".tar.gz";
            string archiveName = $"ivy-agent-cli-{os}-{arch}{extension}";
            string downloadUrl = $"https://cdn.ivy.app/ivy-agent-cli/releases/download/{version}/{archiveName}";
            string archivePath = Path.Combine(tempDir, archiveName);
            
            try
            {
                using var stream = await httpClient.GetStreamAsync(downloadUrl);
                using var fileStream = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await stream.CopyToAsync(fileStream);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to download release archive from CDN: {ex.Message}");
            }
            
            if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(archivePath, tempDir, true);
            }
            else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                var tarInfo = new ProcessStartInfo
                {
                    FileName = "tar",
                    Arguments = $"-xzf \"{archivePath}\" -C \"{tempDir}\"",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var tarProc = Process.Start(tarInfo);
                if (tarProc != null)
                {
                    await tarProc.WaitForExitAsync();
                    if (tarProc.ExitCode != 0)
                    {
                        var tarErr = await tarProc.StandardError.ReadToEndAsync();
                        throw new Exception($"tar extraction failed: {tarErr.Trim()}");
                    }
                }
            }
            
            string binaryName = os == "windows" ? "ivy-agent.exe" : "ivy-agent";
            var files = Directory.GetFiles(tempDir, binaryName, SearchOption.AllDirectories);
            var binarySource = files.FirstOrDefault();
            
            if (string.IsNullOrEmpty(binarySource))
            {
                throw new Exception($"Binary '{binaryName}' not found in the downloaded archive.");
            }
            
            string destPath = Path.Combine(installDir, binaryName);
            if (File.Exists(destPath)) File.Delete(destPath);
            File.Move(binarySource, destPath);
            
            if (os != "windows")
            {
                var chmodInfo = new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{destPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var chmodProc = Process.Start(chmodInfo);
                if (chmodProc != null) await chmodProc.WaitForExitAsync();
            }
            
            if (os == "darwin")
            {
                var codesignInfo = new ProcessStartInfo
                {
                    FileName = "codesign",
                    Arguments = $"-s - --force \"{destPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var codesignProc = Process.Start(codesignInfo);
                if (codesignProc != null) await codesignProc.WaitForExitAsync();
            }
            
            return true;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
            catch {}
        }
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
}
