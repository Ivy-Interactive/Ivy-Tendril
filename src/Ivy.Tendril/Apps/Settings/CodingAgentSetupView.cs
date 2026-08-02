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

    private static readonly AgentInfo[] Agents =
    [
        new("claude", "Claude", AgentBranding.IconFor("claude")),
        new("copilot", "Copilot", AgentBranding.IconFor("copilot")),
        new("codex", "Codex", AgentBranding.IconFor("codex")),
        new("gemini", "Gemini", AgentBranding.IconFor("gemini")),
        new("antigravity", "Antigravity", AgentBranding.IconFor("antigravity")),
        new("opencode", "OpenCode", AgentBranding.IconFor("opencode")),
        new("ivy", "Ivy Agent", AgentBranding.IconFor("ivy"))
    ];

    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();
        var runner = UseService<IAgentRunner>();

        var selectedAgent = UseState(
            string.IsNullOrWhiteSpace(config.Settings.CodingAgent)
                ? "claude"
                : config.Settings.CodingAgent);

        var deepModel = UseState(GetProfileModel(config, selectedAgent.Value, "deep"));
        var balancedModel = UseState(GetProfileModel(config, selectedAgent.Value, "balanced"));
        var quickModel = UseState(GetProfileModel(config, selectedAgent.Value, "quick"));
        var lastAgent = UseState(selectedAgent.Value);
        var showTestDialog = UseState(false);
        var ivyApiKey = UseState(GetIvyApiKeyFromConfig(config));
        var ivyBaseUrl = UseState(GetIvyBaseUrlFromConfig(config));
        var ollamaUrl = UseState(GetOllamaUrlFromConfig(config, selectedAgent.Value));

        var modelsQuery = UseQuery<ModelInfo[], string>(
            selectedAgent.Value,
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
            var hc = runner.GetHealthCheck("ivy");
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

        if (lastAgent.Value != selectedAgent.Value)
        {
            deepModel.Set(GetProfileModel(config, selectedAgent.Value, "deep"));
            balancedModel.Set(GetProfileModel(config, selectedAgent.Value, "balanced"));
            quickModel.Set(GetProfileModel(config, selectedAgent.Value, "quick"));
            ollamaUrl.Set(GetOllamaUrlFromConfig(config, selectedAgent.Value));
            lastAgent.Set(selectedAgent.Value);
        }

        var models = modelsQuery.Value ?? [];
        var modelOptions = new[] { new Option<string>("Default", "default") }
            .Concat(models
                .Where(m => m.Id != "default")
                .Select(m => new Option<string>(m.DisplayName, m.Id)))
            .ToArray<IAnyOption>();

        var hasProfileChanges =
            deepModel.Value != GetProfileModel(config, selectedAgent.Value, "deep") ||
            balancedModel.Value != GetProfileModel(config, selectedAgent.Value, "balanced") ||
            quickModel.Value != GetProfileModel(config, selectedAgent.Value, "quick");
        
        var hasApiKeyChanges = selectedAgent.Value == "ivy" && ivyApiKey.Value != GetIvyApiKeyFromConfig(config);
        var hasBaseUrlChanges = selectedAgent.Value == "ivy" && ivyBaseUrl.Value != GetIvyBaseUrlFromConfig(config);
        var hasOllamaUrlChanges = selectedAgent.Value == "opencode" && ollamaUrl.Value != GetOllamaUrlFromConfig(config, selectedAgent.Value);

        var hasChanges = selectedAgent.Value != config.Settings.CodingAgent || hasProfileChanges || hasApiKeyChanges || hasBaseUrlChanges || hasOllamaUrlChanges;

        var registeredAgents = runner.RegisteredAgents;
        var visibleAgents = Agents.Where(a => registeredAgents.Contains(a.Key)).ToArray();

        var grid = Layout.Grid()
            .Columns(2.At(Breakpoint.Mobile).And(Breakpoint.Desktop, 3))
            .Gap(2);
        grid = visibleAgents.Aggregate(grid, (current, a) =>
            current | new Card(
                Layout.Horizontal().Gap(2).Padding(0)
                | a.Logo.ToIcon().Width(Size.Px(32)).Height(Size.Px(32))
                | Text.Block(a.Label)
                | new Spacer()
                | (a.Key == selectedAgent.Value ? Icons.Check.ToIcon() : null)
            ).Width(Size.Full()).Height(Size.Full()).OnClick(() =>
            {
                selectedAgent.Set(a.Key);
            }));

        return Layout.Vertical().Padding(4)
               | Text.Block("Coding Agent").Bold()
               | (Layout.Vertical().Padding(0)
                   .Width(Size.Full().At(Breakpoint.Mobile).And(Breakpoint.Desktop, Size.Units(170)))
                   | grid.Width(Size.Full()))
               | (Layout.Vertical().Width(Size.Auto().Max(Size.Units(120)))
                   | Text.Block("Profile Models").Bold()
                   | Text.Muted("Promptwares are configured to use different profiles depending on the complexity of the task. You can specify what model to use for each profile.").Small()
                   | deepModel.ToSelectInput(modelOptions).Loading(modelsQuery.Loading).WithField().Label("Deep")
                   | balancedModel.ToSelectInput(modelOptions).Loading(modelsQuery.Loading).WithField().Label("Balanced")
                   | quickModel.ToSelectInput(modelOptions).Loading(modelsQuery.Loading).WithField().Label("Quick"))
               | (selectedAgent.Value == "ivy"
                   ? (object)(Layout.Vertical().Width(Size.Auto().Max(Size.Units(120)))
                       | new Box()
                           .BorderColor(Colors.Warning)
                           .Padding(4)
                           .BorderRadius(BorderRadius.Rounded)
                           .Content(
                               Layout.Vertical().Gap(2)
                               | Text.Block("Ivy Agent Invite-Only Beta").Bold().Color(Colors.Warning)
                               | Text.Rich()
                                   .Run("Ivy Agent is currently in early beta with invite-only access. You can find the installation guide and download the tooling from the ")
                                   .Link("private GitHub repository", "https://github.com/Ivy-Interactive/ivy-agent-cli")
                                   .Run(".")
                                   .OnLinkClick(url => client.OpenUrl(url))
                               | new Spacer().Height(Size.Units(1))
                               | (isIvyInstalled.Value
                                   ? Text.Block("✓ Ivy Agent is installed locally.").Color(Colors.Success).Small()
                                   : (object)(Layout.Vertical().Gap(1)
                                       | new Button(isInstalling.Value ? "Downloading & Installing..." : "One-Click Download & Install")
                                           .Primary()
                                           .Disabled(isInstalling.Value)
                                           .OnClick(async () =>
                                           {
                                               isInstalling.Set(true);
                                               installError.Set(null);
                                               try
                                               {
                                                   await InstallIvyAgentAsync(client);
                                                   await checkIvyInstall();
                                                   client.Toast("Ivy Agent downloaded and installed successfully!", "Success");
                                               }
                                               catch (Exception ex)
                                               {
                                                   installError.Set(ex.Message);
                                               }
                                               finally
                                               {
                                                   isInstalling.Set(false);
                                               }
                                           })
                                       | (installError.Value != null ? Text.Block(installError.Value).Color(Colors.Destructive).Small() : null!)
                                     )
                                 )
                           )
                       | new Spacer().Height(Size.Units(2))
                       | ivyApiKey.ToPasswordInput("sk-...")
                           .WithField()
                           .Label("Ivy Proxy API Key (Optional)")
                           .Description("Optional. If not set, Tendril will use the token from your @ivy.app account login.")
                       | new Spacer().Height(Size.Units(2))
                       | ivyBaseUrl.ToTextInput("https://llmproxy.ivy.app")
                           .WithField()
                           .Label("Ivy Proxy Base URL (Optional)")
                           .Description("Optional. Overrides the default base URL for the Ivy API proxy."))
                   : null!)
               | (selectedAgent.Value == "opencode"
                   ? (object)(Layout.Vertical().Width(Size.Auto().Max(Size.Units(120)))
                       | new Spacer().Height(Size.Units(2))
                       | ollamaUrl.ToTextInput("http://localhost:11434")
                           .WithField()
                           .Label("Ollama Host / Base URL (Optional)")
                           .Description("Optional. Overrides the default Ollama server URL (sets OLLAMA_HOST and OLLAMA_BASE_URL environment variables)."))
                   : null!)
               | new Spacer().Height(Size.Units(4))
               | (Layout.Horizontal().Gap(2)
                   | new Button("Test Agent").Outline()
                       .Disabled(modelsQuery.Loading)
                       .OnClick(() => showTestDialog.Set(true))
                   | new Button("Save").Primary()
                       .Disabled(!hasChanges)
                       .OnClick(() =>
                       {
                           config.Settings.CodingAgent = selectedAgent.Value;
                           SaveProfiles(config, selectedAgent.Value, deepModel.Value, balancedModel.Value, quickModel.Value);
                           SaveIvyApiKey(config, ivyApiKey.Value);
                           SaveIvyBaseUrl(config, ivyBaseUrl.Value);
                           SaveOllamaUrl(config, selectedAgent.Value, ollamaUrl.Value);
                           config.SaveSettings();
                           client.Toast("Coding agent settings saved", "Saved");
                       }))
               | new AgentTestDialog(
                   showTestDialog,
                   selectedAgent,
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

            PathHelper.EnsureIvyAgentCliSetup();
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

}
