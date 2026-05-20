using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

[Description("Update Tendril to the latest version")]
public class UpdateCliCommand : AsyncCommand<UpdateCliCommand.Settings>
{
    public class Settings : CommandSettings
    {
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var currentVersion = GetCurrentVersion();
        var latestVersion = await GetLatestVersionAsync();
        var isUpToDate = currentVersion != null && latestVersion != null && currentVersion == latestVersion;

        if (isUpToDate)
        {
            AnsiConsole.MarkupLine("[green]You are already on the latest version of Ivy.Tendril.[/]");
            return 0;
        }

        AnsiConsole.MarkupLine("[grey]Please wait...[/]");

        var executablePath = await Extract();

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
        };
        Process.Start(psi);

        return 0;
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

    private static (string resourcePath, string executableFileName) GetPlatformResource()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
            return ($"Ivy.Tendril.PublishedBinaries.Ivy.Tendril.Updater.{arch}.zip", "Ivy.Tendril.Updater.exe");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
            return ($"Ivy.Tendril.PublishedBinaries.Ivy.Tendril.Updater.{arch}.zip", "Ivy.Tendril.Updater");
        }

        throw new PlatformNotSupportedException(RuntimeInformation.OSDescription);
    }

    private static async Task<string> Extract()
    {
        var (resourcePath, executableFileName) = GetPlatformResource();

        string tempDir = Path.Combine(Path.GetTempPath(), "Ivy.Tendril.Updater");

        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);

        Directory.CreateDirectory(tempDir);

        string zipPath = Path.Combine(tempDir, "Ivy.Tendril.Updater.zip");

        var assembly = Assembly.GetExecutingAssembly();

        await using (var stream = assembly.GetManifestResourceStream(resourcePath)
                                  ?? throw new InvalidOperationException($"Resource '{resourcePath}' not found."))
        {
            await using var resourceStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write);
            await stream.CopyToAsync(resourceStream);
        }

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Read))
        {
            foreach (var entry in archive.Entries)
            {
                var filePath = Path.Combine(tempDir, entry.Name);
                await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
                await entry.Open().CopyToAsync(fileStream);
            }
        }

        var exePath = Path.Combine(tempDir, executableFileName);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var chmod = new ProcessStartInfo("chmod", $"+x \"{exePath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(chmod)?.WaitForExit();
        }

        return exePath;
    }
}
