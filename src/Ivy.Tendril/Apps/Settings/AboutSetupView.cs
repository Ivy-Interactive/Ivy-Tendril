using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Ivy.Tendril.Agents.Providers.Ivy;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Settings;

public class AboutSetupView : ViewBase
{
    private record SystemEnvironmentInfo(
        string Application,
        string Version,
        string Framework,
        string OperatingSystem,
        string Architecture,
        string Runtime,
        string TendrilHome);

    public record BundledToolInfo(
        string Tool,
        string Status,
        string License,
        string Version,
        string Location);

    public static BundledToolInfo[] GetDefaultToolchain(
        bool isIvyAgentBundled,
        string ivyAgentPath,
        string ivyAgentVersion,
        bool isDotnetBundled,
        string dotnetPath,
        string dotnetVersion,
        bool isPwshBundled,
        string pwshPath,
        string pwshVersion,
        string gitVersion)
    {
        return
        [
            new BundledToolInfo("OpenCode CLI", isIvyAgentBundled ? "Bundled" : File.Exists(ivyAgentPath) ? "Installed" : "Not Found", "MIT License", ivyAgentVersion, ivyAgentPath),
            new BundledToolInfo(".NET SDK / Runtime", isDotnetBundled ? "Bundled SDK" : "Runtime", "MIT License", dotnetVersion, dotnetPath),
            new BundledToolInfo("PowerShell", isPwshBundled ? "Bundled" : File.Exists(pwshPath) ? "System" : "Not Found", "MIT License", pwshVersion, pwshPath),
            new BundledToolInfo("Git", "System Tool", "GPL v2", gitVersion, "git")
        ];
    }

    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var versionService = UseService<IVersionCheckService>();
        var copyToClipboard = UseClipboard();

        var pwshVersionState = UseState("Detecting...");
        var dotnetVersionState = UseState("Detecting...");
        var ivyAgentVersionState = UseState("Detecting...");
        var gitVersionState = UseState("Detecting...");
        var isCheckingUpdates = UseState(false);

        var tendrilVersion = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        var ivyVersion = typeof(ViewBase).Assembly.GetName().Version?.ToString(3) ?? "1.3.21";
        var osDescription = RuntimeInformation.OSDescription;
        var osArch = RuntimeInformation.OSArchitecture.ToString();
        var processArch = RuntimeInformation.ProcessArchitecture.ToString();
        var frameworkDesc = RuntimeInformation.FrameworkDescription;
        var tendrilHome = PathHelper.GetDefaultTendrilHome();

        var pwshPath = PathHelper.GetPwshPath();
        var bundledDotnet = PathHelper.GetBundledDotnetPath();
        var dotnetPath = bundledDotnet ?? PathHelper.GetDotnetPath();
        var ivyAgentPath = IvyBinaryResolver.Resolve();

        var isPwshBundled = pwshPath.Contains(System.AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase)
                            || (OperatingSystem.IsMacOS() && pwshPath.Contains(".app/Contents/Resources", StringComparison.OrdinalIgnoreCase));
        var isDotnetBundled = bundledDotnet != null;
        var isIvyAgentBundled = ivyAgentPath.Contains(System.AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase)
                                || (OperatingSystem.IsMacOS() && ivyAgentPath.Contains(".app/Contents/Resources", StringComparison.OrdinalIgnoreCase));

        UseEffect(async () =>
        {
            var pwshTask = QueryCommandVersionAsync(pwshPath, "--version");
            var dotnetTask = QueryCommandVersionAsync(dotnetPath, "--version");
            var ivyAgentTask = QueryCommandVersionAsync(ivyAgentPath, "--version");
            var gitTask = QueryGitVersionAsync();

            var results = await Task.WhenAll(pwshTask, dotnetTask, ivyAgentTask, gitTask);

            pwshVersionState.Set(FormatVersionOutput(results[0]));
            dotnetVersionState.Set(FormatVersionOutput(results[1]));
            ivyAgentVersionState.Set(FormatVersionOutput(results[2]));
            gitVersionState.Set(FormatVersionOutput(results[3]));
        });

        var ossAttributionCard = new Card(
            Layout.Vertical().Gap(3)
                | Text.Block("Tendril is built on open source technologies and bundles open source software (OSS) runtimes to power its autonomous planning and execution engine.")
                | (Layout.Vertical().Gap(2)
                    | (Layout.Horizontal().Gap(2).AlignContent(Align.Center)
                        | Icons.OpenCode.ToIcon().Width(Size.Px(20)).Height(Size.Px(20))
                        | Text.Block("OpenCode CLI").Bold()
                        | new Badge("MIT License").Variant(BadgeVariant.Outline).Small()
                        | Text.Muted("Bundled open-source coding engine (https://github.com/anomalyco/opencode)").Small())
                    | (Layout.Horizontal().Gap(2).AlignContent(Align.Center)
                        | Icons.Cpu.ToIcon().Width(Size.Px(20)).Height(Size.Px(20))
                        | Text.Block(".NET SDK / Runtime").Bold()
                        | new Badge("MIT License").Variant(BadgeVariant.Outline).Small()
                        | Text.Muted("Application foundation (https://github.com/dotnet/core)").Small())
                    | (Layout.Horizontal().Gap(2).AlignContent(Align.Center)
                        | Icons.Terminal.ToIcon().Width(Size.Px(20)).Height(Size.Px(20))
                        | Text.Block("PowerShell Core").Bold()
                        | new Badge("MIT License").Variant(BadgeVariant.Outline).Small()
                        | Text.Muted("Automation runtime (https://github.com/PowerShell/PowerShell)").Small()))
                | (Layout.Horizontal().Gap(2)
                    | new Button("OpenCode Repository")
                        .Variant(ButtonVariant.Outline)
                        .Icon(Icons.ExternalLink, Align.Right)
                        .OnClick(() => client.OpenUrl("https://github.com/anomalyco/opencode"))
                    | new Button(".NET Repository")
                        .Variant(ButtonVariant.Outline)
                        .Icon(Icons.ExternalLink, Align.Right)
                        .OnClick(() => client.OpenUrl("https://github.com/dotnet/core"))
                    | new Button("PowerShell Repository")
                        .Variant(ButtonVariant.Outline)
                        .Icon(Icons.ExternalLink, Align.Right)
                        .OnClick(() => client.OpenUrl("https://github.com/PowerShell/PowerShell")))
        ).Header("Open Source Software & Attribution", icon: Icons.OpenCode);

        var systemInfo = new SystemEnvironmentInfo(
            Application: "Tendril",
            Version: $"v{tendrilVersion}",
            Framework: $"Ivy Framework v{ivyVersion}",
            OperatingSystem: osDescription,
            Architecture: $"{processArch} (OS: {osArch})",
            Runtime: frameworkDesc,
            TendrilHome: tendrilHome);

        var systemDetailsCard = new Card(
            systemInfo.ToDetails()
                .Label(x => x.Application, "Application")
                .Label(x => x.Version, "Tendril Version")
                .Label(x => x.Framework, "UI Framework")
                .Label(x => x.OperatingSystem, "Operating System")
                .Label(x => x.Architecture, "Architecture")
                .Label(x => x.Runtime, ".NET Runtime")
                .Label(x => x.TendrilHome, "Tendril Home")
                .Builder(x => x.Version, f => f.CopyToClipboard())
                .Builder(x => x.TendrilHome, f => f.CopyToClipboard())
        ).Header("System & Environment", icon: Icons.Cpu);

        var toolsList = GetDefaultToolchain(
            isIvyAgentBundled,
            ivyAgentPath,
            ivyAgentVersionState.Value,
            isDotnetBundled,
            dotnetPath,
            dotnetVersionState.Value,
            isPwshBundled,
            pwshPath,
            pwshVersionState.Value,
            gitVersionState.Value);

        var toolsTableCard = new Card(
            toolsList.ToTable()
                .Width(Size.Full())
                .Header(x => x.Tool, "Software Tool")
                .Header(x => x.Status, "Status")
                .Header(x => x.License, "License")
                .Header(x => x.Version, "Version")
                .Header(x => x.Location, "Binary Path")
                .Builder(x => x.Status, f => f.Func((string status) => status switch
                {
                    "Bundled" or "Bundled SDK" => new Badge(status).Variant(BadgeVariant.Primary).Small(),
                    "Installed" => new Badge(status).Variant(BadgeVariant.Secondary).Small(),
                    "System Tool" or "Runtime" => new Badge(status).Variant(BadgeVariant.Outline).Small(),
                    _ => new Badge(status).Variant(BadgeVariant.Destructive).Small()
                }))
                .Builder(x => x.License, f => f.Func((string license) => new Badge(license).Variant(BadgeVariant.Outline).Small()))
                .Builder(x => x.Version, f => f.CopyToClipboard())
                .Builder(x => x.Location, f => f.CopyToClipboard())
        ).Header("Software Toolchain", icon: Icons.Package);

        var systemReport = new StringBuilder()
            .AppendLine($"Tendril: v{tendrilVersion}")
            .AppendLine($"Ivy Framework: v{ivyVersion}")
            .AppendLine($"OpenCode / Ivy Agent CLI: {ivyAgentVersionState.Value} ({ivyAgentPath})")
            .AppendLine($".NET SDK: {dotnetVersionState.Value} ({dotnetPath})")
            .AppendLine($"PowerShell: {pwshVersionState.Value} ({pwshPath})")
            .AppendLine($"Git: {gitVersionState.Value}")
            .AppendLine($"OS: {osDescription} ({osArch})")
            .AppendLine($"Process Architecture: {processArch}")
            .AppendLine($".NET Runtime: {frameworkDesc}")
            .AppendLine($"Tendril Home: {tendrilHome}")
            .ToString();

        var header = Layout.Vertical().Width(Size.Full().Max(Size.Units(200)))
            | (Layout.Horizontal()
                | (Layout.Vertical()
                    | Text.H2("About Tendril")
                    | Text.Muted("Tendril is an open source AI coding orchestrator built on open source technologies.").Small())
                | new Spacer()
                | new Button("Copy System Report")
                    .Variant(ButtonVariant.Outline)
                    .Icon(Icons.ClipboardCopy)
                    .OnClick(() =>
                    {
                        copyToClipboard(systemReport);
                        client.Toast("System report copied to clipboard", "Copied");
                    })
                | new Button("Check for Updates")
                    .Variant(ButtonVariant.Primary)
                    .Icon(Icons.CircleArrowUp)
                    .Disabled(isCheckingUpdates.Value)
                    .OnClick(() =>
                    {
                        isCheckingUpdates.Set(true);
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var info = await versionService.CheckForUpdatesAsync(forceRefresh: true);
                                if (info.HasUpdate)
                                {
                                    client.Toast($"A new version (v{info.LatestVersion}) is available!", "Update Available");
                                }
                                else if (info.LatestVersion == null)
                                {
                                    client.Toast("Couldn't check for updates. Please try again later.", "Update check failed").Destructive();
                                }
                                else
                                {
                                    client.Toast($"You're running the latest version (v{info.CurrentVersion}).", "Up to date").Success();
                                }
                            }
                            catch (Exception ex)
                            {
                                client.Toast($"Couldn't check for updates: {ex.Message}", "Update check failed").Destructive();
                            }
                            finally
                            {
                                isCheckingUpdates.Set(false);
                            }
                        });
                    }));

        var content = Layout.Vertical().Width(Size.Full().Max(Size.Units(200)))
            | ossAttributionCard
            | systemDetailsCard
            | toolsTableCard;

        return new HeaderLayout(header, content);
    }

    private static async Task<string> QueryCommandVersionAsync(string? executablePath, string arguments)
    {
        if (string.IsNullOrEmpty(executablePath)) return "Not configured";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return "Not available";

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var outputTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
            await proc.WaitForExitAsync(cts.Token);
            var output = (await outputTask).Trim();
            return string.IsNullOrWhiteSpace(output) ? "Available" : output;
        }
        catch
        {
            return File.Exists(executablePath) ? "Available" : "Not installed";
        }
    }

    private static async Task<string> QueryGitVersionAsync()
    {
        try
        {
            var psi = GitHelper.MakeGitStartInfo("--version");
            using var proc = Process.Start(psi);
            if (proc == null) return "Not available";

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var outputTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
            await proc.WaitForExitAsync(cts.Token);
            var output = (await outputTask).Trim();
            return string.IsNullOrWhiteSpace(output) ? "Available" : output;
        }
        catch
        {
            return "Not available";
        }
    }

    private static string FormatVersionOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return "Available";
        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var firstLine = lines.FirstOrDefault()?.Trim() ?? output;
        return firstLine;
    }
}
