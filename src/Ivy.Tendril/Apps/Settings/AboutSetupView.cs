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
    private record SoftwareItem(
        string Name,
        string Version,
        string Status,
        string? Path,
        Icons Icon);

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

        var softwareList = new List<SoftwareItem>
        {
            new("Tendril", $"v{tendrilVersion}", "Application", System.AppContext.BaseDirectory, Icons.Rocket),
            new("Ivy Framework", $"v{ivyVersion}", "Framework", null, Icons.Component),
            new("Ivy Agent CLI", ivyAgentVersionState.Value, isIvyAgentBundled ? "Bundled" : File.Exists(ivyAgentPath) ? "Installed" : "Not Found", ivyAgentPath, Icons.Bot),
            new(".NET SDK / Runtime", dotnetVersionState.Value, isDotnetBundled ? "Bundled SDK" : "Runtime", dotnetPath, Icons.Cpu),
            new("PowerShell", pwshVersionState.Value, isPwshBundled ? "Bundled" : File.Exists(pwshPath) ? "System" : "Not Found", pwshPath, Icons.Terminal),
            new("Git", gitVersionState.Value, "System Tool", "git", Icons.GitBranch),
        };

        var softwareRows = softwareList.Select(item =>
            (Layout.Horizontal()
                | item.Icon.ToIcon()
                | (Layout.Vertical()
                    | (Layout.Horizontal()
                        | Text.Block(item.Name).Bold()
                        | (item.Status == "Bundled" || item.Status == "Bundled SDK" || item.Status == "Application"
                            ? new Badge(item.Status).Variant(BadgeVariant.Primary).Small()
                            : new Badge(item.Status).Variant(BadgeVariant.Secondary).Small()))
                    | (Layout.Horizontal()
                        | Text.Muted($"Version: {item.Version}").Small()
                        | (item.Path != null ? Text.Muted($"• Path: {item.Path}").Small() : null))))
        ).ToArray();

        var systemInfoText = new StringBuilder()
            .AppendLine($"Tendril: v{tendrilVersion}")
            .AppendLine($"Ivy Framework: v{ivyVersion}")
            .AppendLine($"Ivy Agent CLI: {ivyAgentVersionState.Value} ({ivyAgentPath})")
            .AppendLine($".NET SDK: {dotnetVersionState.Value} ({dotnetPath})")
            .AppendLine($"PowerShell: {pwshVersionState.Value} ({pwshPath})")
            .AppendLine($"Git: {gitVersionState.Value}")
            .AppendLine($"OS: {osDescription} ({osArch})")
            .AppendLine($"Process Architecture: {processArch}")
            .AppendLine($".NET Framework: {frameworkDesc}")
            .AppendLine($"Tendril Home: {tendrilHome}")
            .ToString();

        return Layout.Vertical().Width(Size.Auto().Max(Size.Units(120)))
               | Text.Block("About Tendril").Bold()
               | Text.Block("Software versions and bundled environment details.").Muted().Small()
               | (Layout.Horizontal()
                  | new Button("Copy System Info")
                      .Variant(ButtonVariant.Outline)
                      .Icon(Icons.ClipboardCopy)
                      .OnClick(() =>
                      {
                          copyToClipboard(systemInfoText);
                          client.Toast("System and software information copied to clipboard", "Copied");
                      })
                  | new Button("Check for Updates")
                      .Variant(ButtonVariant.Outline)
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
                      }))
               | new Separator()
               | Text.Block("Software & Bundled Components").Bold()
               | Layout.Vertical(softwareRows)
               | new Separator()
               | Text.Block("Environment").Bold()
               | (Layout.Vertical()
                  | (Layout.Horizontal() | Text.Muted("Operating System:").Small() | Text.Block(osDescription).Small())
                  | (Layout.Horizontal() | Text.Muted("OS Architecture:").Small() | Text.Block(osArch).Small())
                  | (Layout.Horizontal() | Text.Muted("Process Architecture:").Small() | Text.Block(processArch).Small())
                  | (Layout.Horizontal() | Text.Muted(".NET Framework:").Small() | Text.Block(frameworkDesc).Small())
                  | (Layout.Horizontal() | Text.Muted("Tendril Home:").Small() | Text.Block(tendrilHome).Small()));
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
