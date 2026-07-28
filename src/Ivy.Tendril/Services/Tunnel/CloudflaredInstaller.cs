using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Ivy.Tendril.Agents.Helpers;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Tunnel;

public sealed class CloudflaredInstaller
{
    private readonly string _toolsDirectory;
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;

    public CloudflaredInstaller(string tendrilHome, IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _toolsDirectory = Path.Combine(tendrilHome, "tools");
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
    }

    public async Task<string> EnsureInstalledAsync(CancellationToken ct = default)
    {
        var existing = FindExisting();
        if (existing is not null)
        {
            _logger.LogDebug("Found cloudflared at {Path}", existing);
            return existing;
        }

        return await DownloadAsync(ct);
    }

    public string? FindExisting()
    {
        var localPath = GetLocalBinaryPath();
        if (File.Exists(localPath))
            return localPath;

        return BinaryResolver.FindOnPath("cloudflared");
    }

    public async Task<string> DownloadAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_toolsDirectory);

        var binaryName = GetPlatformBinaryName();
        var url = GetDownloadUrl(binaryName);
        var targetPath = GetLocalBinaryPath();

        _logger.LogInformation("Downloading cloudflared from {Url}", url);

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);

        if (binaryName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Decompressing tarball and extracting cloudflared binary");
            await using var gzipStream = new GZipStream(stream, CompressionMode.Decompress);
            using var tarReader = new TarReader(gzipStream);
            var extracted = false;
            while (tarReader.GetNextEntry() is TarEntry entry)
            {
                if (entry.Name == "cloudflared" || entry.Name.EndsWith("/cloudflared", StringComparison.OrdinalIgnoreCase))
                {
                    if (entry.DataStream is not null)
                    {
                        await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
                        await entry.DataStream.CopyToAsync(fileStream, ct);
                        extracted = true;
                        break;
                    }
                }
            }

            if (!extracted)
            {
                throw new FileNotFoundException("Could not find 'cloudflared' binary inside the downloaded archive.");
            }
        }
        else
        {
            await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(fileStream, ct);
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var chmod = Process.Start(new ProcessStartInfo
            {
                FileName = "chmod",
                ArgumentList = { "+x", targetPath },
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            chmod?.WaitForExit(5000);
        }

        _logger.LogInformation("Installed cloudflared to {Path}", targetPath);
        return targetPath;
    }

    public static string GetPlatformBinaryName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "cloudflared-windows-amd64.exe";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "cloudflared-darwin-arm64.tgz"
                : "cloudflared-darwin-amd64.tgz";

        return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? "cloudflared-linux-arm64"
            : "cloudflared-linux-amd64";
    }

    public static string GetDownloadUrl(string binaryName) =>
        $"https://github.com/cloudflare/cloudflared/releases/latest/download/{binaryName}";

    private string GetLocalBinaryPath()
    {
        var localName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "cloudflared.exe"
            : "cloudflared";
        return Path.Combine(_toolsDirectory, localName);
    }
}
