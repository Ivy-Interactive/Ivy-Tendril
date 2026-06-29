using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ivy.Tendril.Helpers;

public static class UpdateHelper
{
    public static async Task<string?> GetLatestVersionAsync()
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Ivy.Tendril");
            var response = await httpClient.GetStringAsync("https://api.nuget.org/v3-flatcontainer/ivy.tendril/index.json");
            using var json = JsonDocument.Parse(response);
            if (json.RootElement.TryGetProperty("versions", out var versions))
            {
                var highest = new Version(0, 0, 0);
                string? highestStr = null;
                foreach (var v in versions.EnumerateArray())
                {
                    var versionStr = v.GetString();
                    if (string.IsNullOrEmpty(versionStr) || versionStr.Contains('-')) continue;
                    if (Version.TryParse(versionStr, out var parsed) && parsed > highest)
                    {
                        highest = parsed;
                        highestStr = versionStr;
                    }
                }
                return highestStr;
            }
        }
        catch
        {
            // Ignore
        }
        return null;
    }

    public static (string filename, string url) GetInstallerInfo(string version)
    {
        string os;
        string ext;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            os = "win";
            ext = "exe";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            os = "osx";
            ext = "pkg";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            os = "linux";
            ext = "AppImage";
        }
        else
        {
            throw new PlatformNotSupportedException($"Unsupported OS: {RuntimeInformation.OSDescription}");
        }

        string arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        string filename = $"IvyTendril-{version}-{os}-{arch}.{ext}";
        string url = $"https://github.com/Ivy-Interactive/Ivy-Tendril/releases/download/v{version}/{filename}";

        return (filename, url);
    }

    public static async Task DownloadFileWithProgressAsync(string url, string destinationPath, Action<long, long>? progressCallback, CancellationToken cancellationToken = default)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", "Ivy.Tendril");
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        var totalRead = 0L;
        int read;

        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
            totalRead += read;
            progressCallback?.Invoke(totalRead, totalBytes);
        }
    }

    public static void LaunchInstaller(string tempFilePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo(tempFilePath) { UseShellExecute = true });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", $"\"{tempFilePath}\"");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                var chmod = new ProcessStartInfo("chmod", $"+x \"{tempFilePath}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(chmod)?.WaitForExit();
            }
            catch
            {
                // Ignore
            }
            Process.Start(new ProcessStartInfo(tempFilePath) { UseShellExecute = true });
        }
    }
}
