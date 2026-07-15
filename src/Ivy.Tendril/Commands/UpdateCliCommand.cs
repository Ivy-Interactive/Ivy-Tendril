using System.ComponentModel;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using Velopack;
using Velopack.Sources;
using Velopack.Locators;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Commands;

[Description("Update Tendril to the latest version")]
public class UpdateCliCommand : AsyncCommand<UpdateCliCommand.Settings>
{
    public class Settings : CommandSettings
    {
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var includeBeta = Environment.GetEnvironmentVariable("TENDRIL_BETA") == "1";
        var source = new GithubSource("https://github.com/Ivy-Interactive/Ivy-Tendril", null, includeBeta);
        var mgr = new UpdateManager(source);

        if (mgr.IsInstalled)
        {
            var currentChannel = VelopackLocator.Current?.Channel ?? "win";
            var targetChannel = currentChannel;

            if (includeBeta && !currentChannel.EndsWith("-beta"))
            {
                targetChannel = $"{currentChannel}-beta";
            }
            else if (!includeBeta && currentChannel.EndsWith("-beta"))
            {
                targetChannel = currentChannel.Substring(0, currentChannel.Length - "-beta".Length);
            }

            if (targetChannel != currentChannel)
            {
                var options = new UpdateOptions
                {
                    ExplicitChannel = targetChannel,
                    AllowVersionDowngrade = true
                };
                mgr = new UpdateManager(source, options);
            }

            AnsiConsole.MarkupLine("[grey]Checking for updates via Velopack...[/]");
            UpdateInfo? updateInfo = null;

            await AnsiConsole.Status()
                .StartAsync("Connecting to release server...", async ctx =>
                {
                    updateInfo = await mgr.CheckForUpdatesAsync();
                });

            if (updateInfo == null)
            {
                AnsiConsole.MarkupLine("[green]You are already on the latest version of Ivy.Tendril.[/]");
                return 0;
            }

            AnsiConsole.MarkupLine($"[green]New version {updateInfo.TargetFullRelease.Version} is available![/]");

            await AnsiConsole.Progress()
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("[green]Downloading update...[/]", autoStart: true, maxValue: 100);

                    await mgr.DownloadUpdatesAsync(updateInfo, progress => task.Value = progress);
                });

            AnsiConsole.MarkupLine("[green]Update downloaded successfully. Restarting application to apply...[/]");
            mgr.ApplyUpdatesAndRestart(updateInfo);
            return 0;
        }

        var currentVersion = GetCurrentVersion();
        var latestVersion = await GetLatestVersionAsync();
        var isUpToDate = currentVersion != null && latestVersion != null && currentVersion == latestVersion;

        if (isUpToDate)
        {
            AnsiConsole.MarkupLine("[green]You are already on the latest version of Ivy.Tendril.[/]");
            return 0;
        }

        if (latestVersion == null)
        {
            AnsiConsole.MarkupLine("[red]Could not determine the latest version of Ivy.Tendril.[/]");
            return 1;
        }

        var versionStr = latestVersion.ToString(3);
        var (filename, downloadUrl) = UpdateHelper.GetInstallerInfo(versionStr);
        var tempFilePath = Path.Combine(Path.GetTempPath(), filename);

        AnsiConsole.MarkupLine($"[grey]Downloading {filename}...[/]");

        try
        {
            await AnsiConsole.Progress()
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("[green]Downloading installer[/]", autoStart: true, maxValue: 100);
                    
                    await UpdateHelper.DownloadFileWithProgressAsync(downloadUrl, tempFilePath, (read, total) =>
                    {
                        if (total > 0)
                        {
                            task.Value = (int)((double)read / total * 100);
                        }
                    }, cancellationToken);
                });

            AnsiConsole.MarkupLine($"[green]Download complete. Launching installer...[/]");
            UpdateHelper.LaunchInstaller(tempFilePath);
            
            AnsiConsole.MarkupLine("[green]Exiting Tendril to allow installation.[/]");
            Environment.Exit(0);
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to update: {ex.Message}[/]");
            return 1;
        }
    }

    private static Version? GetCurrentVersion()
    {
        try
        {
            var assembly = typeof(Program).Assembly;
            return NormalizeVersion(assembly.GetName().Version?.ToString());
        }
        catch
        {
            return null;
        }
    }

    private static async Task<Version?> GetLatestVersionAsync()
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Ivy.Tendril");

            var response = await httpClient.GetStringAsync("https://api.nuget.org/v3-flatcontainer/ivy.tendril/index.json");
            var json = JsonDocument.Parse(response);

            if (json.RootElement.TryGetProperty("versions", out var versions))
            {
                var versionArray = versions.EnumerateArray().ToArray();
                return NormalizeVersion(versionArray.LastOrDefault().GetString());
            }
        }
        catch
        {
        }

        return null;
    }

    private static Version? NormalizeVersion(string? versionString)
    {
        if (string.IsNullOrEmpty(versionString))
            return null;

        var parts = versionString.Split('.');
        var major = parts.Length > 0 ? int.Parse(parts[0]) : 0;
        var minor = parts.Length > 1 ? int.Parse(parts[1]) : 0;
        var build = parts.Length > 2 ? int.Parse(parts[2]) : 0;
        var revision = parts.Length > 3 ? int.Parse(parts[3]) : 0;

        return new Version(major, minor, build, revision);
    }

}
