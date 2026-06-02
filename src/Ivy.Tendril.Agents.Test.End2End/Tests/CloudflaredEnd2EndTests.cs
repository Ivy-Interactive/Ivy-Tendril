using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
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

    [SkippableFact]
    public async Task QuickTunnel_ProxiesTrafficToOrigin()
    {
        Skip.IfNot(BinaryResolver.IsInstalled("cloudflared"),
            "cloudflared is not installed on this machine");

        var port = GetAvailablePort();
        const string expectedBody = "TENDRIL_TUNNEL_OK";
        var listener = StartTcpOrigin(port, expectedBody);

        try
        {
            var binaryPath = BinaryResolver.FindOnPath("cloudflared")!;
            using var session = new TunnelSession(binaryPath, $"http://localhost:{port}",
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

            using var startCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var tunnelUrl = await session.StartAsync(startCts.Token);

            Assert.Contains("trycloudflare.com", tunnelUrl);
            await AssertTunnelProxiesContent(tunnelUrl, expectedBody);
        }
        finally
        {
            listener.Stop();
        }
    }

    [SkippableFact]
    public async Task QuickTunnel_Returns502WhenOriginNotListening()
    {
        Skip.IfNot(BinaryResolver.IsInstalled("cloudflared"),
            "cloudflared is not installed on this machine");

        var port = GetAvailablePort();
        var binaryPath = BinaryResolver.FindOnPath("cloudflared")!;

        using var session = new TunnelSession(binaryPath, $"http://localhost:{port}",
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var tunnelUrl = await session.StartAsync(cts.Token);

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        await Task.Delay(3000);

        HttpResponseMessage? response = null;
        try
        {
            response = await httpClient.GetAsync(tunnelUrl);
        }
        catch (HttpRequestException) { }

        if (response != null)
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

        session.Stop();
    }

    [SkippableFact]
    public async Task QuickTunnel_EventuallyProxiesWhenOriginStartsLate()
    {
        Skip.IfNot(BinaryResolver.IsInstalled("cloudflared"),
            "cloudflared is not installed on this machine");

        var port = GetAvailablePort();
        var binaryPath = BinaryResolver.FindOnPath("cloudflared")!;

        using var session = new TunnelSession(binaryPath, $"http://localhost:{port}",
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        using var startCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var tunnelUrl = await session.StartAsync(startCts.Token);

        await Task.Delay(3000);

        const string expectedBody = "LATE_START_OK";
        var listener = StartTcpOrigin(port, expectedBody);

        try
        {
            await AssertTunnelProxiesContent(tunnelUrl, expectedBody);
        }
        finally
        {
            listener.Stop();
            session.Stop();
        }
    }

    private static async Task AssertTunnelProxiesContent(string tunnelUrl, string expectedBody)
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        HttpResponseMessage? response = null;
        string? body = null;
        string? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                response = await httpClient.GetAsync(tunnelUrl);
                body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode && body.Contains(expectedBody))
                    break;
                lastError = $"HTTP {(int)response.StatusCode}: {body?[..Math.Min(body?.Length ?? 0, 100)]}";
                response = null;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }

            await Task.Delay(2000);
        }

        Assert.NotNull(response ?? throw new Exception(
            $"Tunnel never proxied successfully within 60s. Last error: {lastError}"));
        Assert.True(response!.IsSuccessStatusCode,
            $"Expected 2xx but got {(int)response.StatusCode}. Body: {body?[..Math.Min(body?.Length ?? 0, 200)]}");
        Assert.Contains(expectedBody, body!);
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static TcpListener StartTcpOrigin(int port, string responseBody)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        _ = AcceptLoop(listener, responseBody);
        return listener;
    }

    private static async Task AcceptLoop(TcpListener listener, string responseBody)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(responseBody);
        var httpResponse = $"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
        var responseBytes = Encoding.UTF8.GetBytes(httpResponse).Concat(bodyBytes).ToArray();

        while (true)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(); }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }

            _ = Task.Run(async () =>
            {
                try
                {
                    await using var stream = client.GetStream();
                    var buffer = new byte[4096];
                    _ = await stream.ReadAsync(buffer);
                    await stream.WriteAsync(responseBytes);
                    await stream.FlushAsync();
                }
                finally { client.Dispose(); }
            });
        }
    }

    private sealed class SimpleHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
