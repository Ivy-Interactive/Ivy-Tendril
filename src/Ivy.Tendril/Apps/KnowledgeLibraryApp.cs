using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using System.Reactive.Disposables;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps;

[App(title: "Knowledge Library", icon: Icons.Library, group: ["Apps"], order: Constants.KnowledgeLibrary)]
public class KnowledgeLibraryApp : ViewBase
{
    private record VaultStatusInfo(
        string VaultPath,
        int TotalMemories,
        int OutdatedMemories,
        int BrokenWikiLinks,
        int OrphanMemories,
        int IncompleteTemplates,
        string RawOutput
    );

    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();
        var vaultStatus = UseState<VaultStatusInfo?>(null);
        var isLoading = UseState(true);
        var uiProcess = UseState<Process?>(null);
        var uiUrl = UseState<string?>(null);

        // Find the vault directory
        var workspaceDir = config.Projects.FirstOrDefault()?.RepoPaths.FirstOrDefault();
        var workingDir = string.IsNullOrEmpty(workspaceDir) ? Directory.GetCurrentDirectory() : Path.GetDirectoryName(workspaceDir) ?? Directory.GetCurrentDirectory();
        var vaultPath = PromptwareHelper.ResolveBrainwaresVaultDir(workingDir);

        void LoadStatus()
        {
            isLoading.Set(true);
            _ = Task.Run(async () =>
            {
                var status = await RunBwStatusAsync(workingDir);
                vaultStatus.Set(status);
                isLoading.Set(false);
            });
        }

        // Load status on mount
        UseEffect(() =>
        {
            LoadStatus();
            return Disposable.Empty;
        }, EffectTrigger.OnMount());

        // Dispose of UI process on unmount
        UseEffect(() =>
        {
            return Disposable.Create(() =>
            {
                if (uiProcess.Value != null)
                {
                    try
                    {
                        uiProcess.Value.Kill(true);
                    }
                    catch
                    {
                        // Best effort
                    }
                }
            });
        });

        // Read brainwares config.json
        string? defaultVaultDir = null;
        string[]? ignorePatterns = null;
        if (vaultPath != null)
        {
            var configPath = Path.Combine(vaultPath, "config.json");
            if (File.Exists(configPath))
            {
                try
                {
                    var json = File.ReadAllText(configPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("default_vault_dir", out var valDir))
                        defaultVaultDir = valDir.GetString();
                    if (doc.RootElement.TryGetProperty("ignore_patterns", out var valPatterns) && valPatterns.ValueKind == JsonValueKind.Array)
                    {
                        ignorePatterns = valPatterns.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
                    }
                }
                catch
                {
                    // Ignore parsing error
                }
            }
        }

        var header = Layout.Vertical().Gap(1)
            | Text.H1("Knowledge Library")
            | Text.Muted("Explore, verify, and launch the shared memory vault configuration and index status.");

        if (vaultPath == null)
        {
            return Layout.Vertical().Padding(new Responsive<Thickness?> { Mobile = new Thickness(6, 0, 6, 0) })
                   | header
                   | new Spacer().Height(Size.Units(5))
                   | (Layout.Center()
                      | Text.Danger("No .brainwares vault directory detected in the active workspace.").Bold()
                      | Text.Muted("Run 'bw init' in the workspace root to initialize one."));
        }

        object statusContent;
        if (isLoading.Value)
        {
            statusContent = Layout.Center().Margin(0, 40)
                            | Text.Muted("Scanning vault references & validating hashes...");
        }
        else if (vaultStatus.Value == null)
        {
            statusContent = Text.Danger("Failed to read vault status.");
        }
        else
        {
            var status = vaultStatus.Value;
            var isClean = status.OutdatedMemories == 0 && status.BrokenWikiLinks == 0;

            var statsGrid = Layout.Horizontal().Gap(4).Wrap(true)
                | new Card(
                      Layout.Vertical().AlignContent(Align.Center)
                      | Text.H2(status.TotalMemories.ToString()).Bold()
                      | Text.Muted("Total Memories").Small()
                  ).Width(Size.Units(40))
                | new Card(
                      Layout.Vertical().AlignContent(Align.Center)
                      | Text.H2(status.OutdatedMemories.ToString()).Bold()
                      | (status.OutdatedMemories > 0 ? Text.Danger("Outdated").Bold() : Text.Success("Up to date")).Small()
                  ).Width(Size.Units(40))
                | new Card(
                      Layout.Vertical().AlignContent(Align.Center)
                      | Text.H2(status.BrokenWikiLinks.ToString()).Bold()
                      | (status.BrokenWikiLinks > 0 ? Text.Danger("Broken Links").Bold() : Text.Success("Clean Links")).Small()
                  ).Width(Size.Units(40))
                | new Card(
                      Layout.Vertical().AlignContent(Align.Center)
                      | Text.H2(status.OrphanMemories.ToString()).Bold()
                      | Text.Muted("Orphans").Small()
                  ).Width(Size.Units(40));

            statusContent = Layout.Vertical().Gap(4)
                | (Layout.Horizontal().Gap(2).AlignContent(Align.Center)
                   | Text.H2("Vault Status").Bold()
                   | (isClean
                      ? new Badge("Vault Verified").Variant(BadgeVariant.Success).Small()
                      : new Badge("Attention Required").Variant(BadgeVariant.Destructive).Small()))
                | statsGrid
                | new Spacer().Height(Size.Units(2))
                | (Layout.Horizontal().Gap(2)
                   | new Button("Refresh Status")
                       .Outline()
                       .Icon(Icons.RefreshCw)
                       .OnClick(LoadStatus)
                   | (uiProcess.Value == null
                      ? new Button("Launch Web UI")
                          .Primary()
                          .Icon(Icons.ExternalLink)
                          .OnClick(() =>
                          {
                              try
                              {
                                  var bwPath = GetBwPath();
                                  var proc = Process.Start(new ProcessStartInfo
                                  {
                                      FileName = bwPath,
                                      Arguments = "ui",
                                      WorkingDirectory = workingDir,
                                      UseShellExecute = false,
                                      CreateNoWindow = true
                                  });
                                  uiProcess.Set(proc);
                                  uiUrl.Set("http://localhost:5173");
                                  client.OpenUrl("http://localhost:5173");
                                  client.Toast("Memories UI launched on http://localhost:5173", "UI Started");
                              }
                              catch (Exception ex)
                              {
                                  client.Toast($"Failed to launch Web UI: {ex.Message}", "Error");
                              }
                          })
                      : new Button("Stop Web UI")
                          .Variant(ButtonVariant.Destructive)
                          .Icon(Icons.Square)
                          .OnClick(() =>
                          {
                              if (uiProcess.Value != null)
                              {
                                  try { uiProcess.Value.Kill(true); } catch { }
                                  uiProcess.Set(null);
                                  uiUrl.Set(null);
                                  client.Toast("Memories UI stopped", "UI Deactivated");
                              }
                          })));
        }

        var configContent = Layout.Vertical().Gap(2)
            | Text.H2("Configuration").Bold()
            | (Layout.Vertical().Gap(1)
               | (Layout.Horizontal().Gap(2)
                  | Text.Bold("Vault Directory:")
                  | Text.Muted(vaultPath))
               | (Layout.Horizontal().Gap(2)
                  | Text.Bold("Default Directory Key:")
                  | Text.Muted(defaultVaultDir ?? ".brainwares"))
               | (ignorePatterns != null && ignorePatterns.Length > 0
                  ? Layout.Vertical().Gap(1)
                    | Text.Bold("Ignore Patterns:")
                    | Layout.Horizontal().Gap(2).Wrap(true)
                      | new Fragment(ignorePatterns.Select(p => new Badge(p).Variant(BadgeVariant.Secondary).Small() as object).ToArray())
                  : null));

        return Layout.Vertical().Padding(new Responsive<Thickness?> { Mobile = new Thickness(6, 0, 6, 0) }).Gap(6)
               | header
               | new Separator()
               | statusContent
               | new Separator()
               | configContent;
    }

    private static string GetBwPath()
    {
        var paths = new[]
        {
            "bw",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cargo", "bin", "bw"),
            "/usr/local/bin/bw",
            "/usr/bin/bw",
            "/opt/homebrew/bin/bw"
        };

        foreach (var p in paths)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = p,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit(1000);
                    if (proc.ExitCode == 0) return p;
                }
            }
            catch { /* skip */ }
        }

        return "bw";
    }

    private static async Task<VaultStatusInfo?> RunBwStatusAsync(string workingDirectory)
    {
        try
        {
            var bwPath = GetBwPath();
            var psi = new ProcessStartInfo
            {
                FileName = bwPath,
                Arguments = "status",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return null;

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            await proc.WaitForExitAsync();

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (proc.ExitCode != 0)
            {
                return new VaultStatusInfo("", 0, 0, 0, 0, 0, $"Error: {stderr}\n{stdout}");
            }

            var vaultPath = "";
            var totalMemories = 0;
            var outdated = 0;
            var broken = 0;
            var orphan = 0;
            var incomplete = 0;

            var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("Vault path:"))
                {
                    vaultPath = line.Substring("Vault path:".Length).Trim(' ', '"');
                }
                else if (line.StartsWith("Total memories:"))
                {
                    int.TryParse(line.Substring("Total memories:".Length).Trim(), out totalMemories);
                }
                else if (line.StartsWith("Outdated memories:"))
                {
                    int.TryParse(line.Substring("Outdated memories:".Length).Trim(), out outdated);
                }
                else if (line.StartsWith("Broken wiki-links:"))
                {
                    int.TryParse(line.Substring("Broken wiki-links:".Length).Trim(), out broken);
                }
                else if (line.StartsWith("Orphan memories:"))
                {
                    int.TryParse(line.Substring("Orphan memories:".Length).Trim(), out orphan);
                }
                else if (line.StartsWith("Incomplete templates:"))
                {
                    int.TryParse(line.Substring("Incomplete templates:".Length).Trim(), out incomplete);
                }
            }

            return new VaultStatusInfo(vaultPath, totalMemories, outdated, broken, orphan, incomplete, stdout);
        }
        catch (Exception ex)
        {
            return new VaultStatusInfo("", 0, 0, 0, 0, 0, $"Exception running bw status: {ex.Message}");
        }
    }
}
