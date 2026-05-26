using System.Diagnostics;
using Ivy.Tendril.Agents.Helpers;
using Ivy.Tendril.Services.Tunnel;

namespace Ivy.Tendril.Agents.Test.End2End.Tests;

public class CloudflaredEnd2EndTests
{
    [Fact]
    public void GetPlatformBinaryName_ReturnsNonEmpty()
    {
        var name = CloudflaredInstaller.GetPlatformBinaryName();
        Assert.NotEmpty(name);
        Assert.Contains("cloudflared", name);
    }

    [Fact]
    public void GetDownloadUrl_ContainsGitHubReleasesPath()
    {
        var url = CloudflaredInstaller.GetDownloadUrl("cloudflared-linux-amd64");
        Assert.Contains("github.com/cloudflare/cloudflared/releases", url);
        Assert.Contains("cloudflared-linux-amd64", url);
    }

    [Fact]
    public void ParseTunnelUrl_ValidLine_ExtractsUrl()
    {
        var line = "2024-01-15T10:00:00Z INF +-------------------------------------------+";
        Assert.Null(TunnelSession.ParseTunnelUrl(line));

        var urlLine = "2024-01-15T10:00:00Z INF |  https://random-words-here.trycloudflare.com  |";
        var result = TunnelSession.ParseTunnelUrl(urlLine);
        Assert.Equal("https://random-words-here.trycloudflare.com", result);
    }

    [Fact]
    public void ParseTunnelUrl_NoUrl_ReturnsNull()
    {
        Assert.Null(TunnelSession.ParseTunnelUrl("Starting tunnel..."));
        Assert.Null(TunnelSession.ParseTunnelUrl(""));
        Assert.Null(TunnelSession.ParseTunnelUrl("https://example.com"));
    }

    [Fact]
    public void ParseTunnelUrl_VariousFormats_Parses()
    {
        Assert.NotNull(TunnelSession.ParseTunnelUrl(
            "Visit it at: https://abc-def-123.trycloudflare.com"));

        Assert.NotNull(TunnelSession.ParseTunnelUrl(
            "https://my-tunnel-xyz.trycloudflare.com/"));

        Assert.NotNull(TunnelSession.ParseTunnelUrl(
            "| https://a.trycloudflare.com |"));
    }

    [SkippableFact]
    public async Task VersionCheck_InstalledBinary_ReturnsVersion()
    {
        Skip.IfNot(BinaryResolver.IsInstalled("cloudflared"),
            "cloudflared is not installed on this machine");

        var binaryPath = BinaryResolver.FindOnPath("cloudflared")!;

        var psi = new ProcessStartInfo
        {
            FileName = binaryPath,
            ArgumentList = { "--version" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var output = stdout + stderr;
        Assert.Equal(0, process.ExitCode);
        Assert.Contains("cloudflared", output, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task QuickTunnel_StartsAndReturnsTrycloudflareUrl()
    {
        Skip.IfNot(BinaryResolver.IsInstalled("cloudflared"),
            "cloudflared is not installed on this machine");

        var binaryPath = BinaryResolver.FindOnPath("cloudflared")!;

        using var session = new TunnelSession(binaryPath, "http://localhost:19999", Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var url = await session.StartAsync(cts.Token);

        Assert.NotNull(url);
        Assert.Contains("trycloudflare.com", url);
        Assert.True(session.IsRunning);

        session.Stop();
        Assert.False(session.IsRunning);
    }

    [SkippableFact]
    public async Task Install_DownloadsBinaryForCurrentPlatform()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cloudflared-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var httpFactory = new SimpleHttpClientFactory();
            var installer = new CloudflaredInstaller(tempDir, httpFactory, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            string path;
            try
            {
                path = await installer.DownloadAsync(cts.Token);
            }
            catch (HttpRequestException ex)
            {
                Skip.If(true, $"Network unavailable: {ex.Message}");
                return;
            }

            Assert.True(File.Exists(path));
            var fileInfo = new FileInfo(path);
            Assert.True(fileInfo.Length > 1_000_000, "Binary should be at least 1MB");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); }
            catch { /* cleanup best-effort */ }
        }
    }

    private sealed class SimpleHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
