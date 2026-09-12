using Ivy.Tendril.Controllers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Tunnel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

namespace Ivy.Tendril.Test;

public class LocalFileGuardMiddlewareTests
{
    private class StubShareTunnelService : IShareTunnelService
    {
        public string? TunnelUrl { get; set; }
        public TunnelStatus Status { get; set; }
        public bool IsConnected { get; set; }
        public bool IsInstalled => false;
        public string? ErrorMessage => null;
        public int SharePort => 0;

        public event Action<TunnelStatus>? StatusChanged { add { } remove { } }

        public void Start() { }
        public Task<bool> CheckInstalledAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task InstallAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ActivateAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DeactivateAsync() => Task.CompletedTask;
        public string GetShareUrlForPlan(string planId, bool relative = false) => "";
        public void Dispose() { }
    }

    private class NextCalledTracker
    {
        public bool Called { get; set; }
    }

    private const string TendrilHome = "/tmp";

    private static (LocalFileGuardMiddleware middleware, DefaultHttpContext context, NextCalledTracker tracker, ConfigService config) CreateMiddleware(
        TendrilSettings? settings = null,
        string? tunnelUrl = null,
        bool tunnelConnected = false,
        string? tendrilHome = null)
    {
        var config = new ConfigService(settings ?? new TendrilSettings(), tendrilHome ?? TendrilHome);
        var tunnelService = new StubShareTunnelService
        {
            TunnelUrl = tunnelUrl,
            IsConnected = tunnelConnected
        };

        var tracker = new NextCalledTracker();
        RequestDelegate next = _ =>
        {
            tracker.Called = true;
            return Task.CompletedTask;
        };

        var middleware = new LocalFileGuardMiddleware(
            next,
            config,
            tunnelService,
            NullLogger<LocalFileGuardMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        return (middleware, context, tracker, config);
    }

    [Fact]
    public async Task UnrelatedPath_PassesThrough()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/api/plans/00001";
        context.Request.Host = new HostString("localhost", 5000);

        await middleware.InvokeAsync(context);

        Assert.True(tracker.Called);
    }

    [Fact]
    public async Task LocalhostWithImageExtension_CallsNext()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.QueryString = new QueryString($"?path={TendrilHome}/test/image.png");

        await middleware.InvokeAsync(context);

        Assert.True(tracker.Called);
    }

    [Fact]
    public async Task Loopback127_0_0_1_CallsNext()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("127.0.0.1", 5000);
        context.Request.QueryString = new QueryString($"?path={TendrilHome}/test/image.png");

        await middleware.InvokeAsync(context);

        Assert.True(tracker.Called);
    }

    [Fact]
    public async Task LoopbackIpv6_CallsNext()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("[::1]", 5000);
        context.Request.QueryString = new QueryString($"?path={TendrilHome}/test/image.png");

        await middleware.InvokeAsync(context);

        Assert.True(tracker.Called);
    }

    [Fact]
    public async Task DisallowedHost_Returns403()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("evil.example.com", 5000);
        context.Request.QueryString = new QueryString($"?path={TendrilHome}/test/image.png");

        await middleware.InvokeAsync(context);

        Assert.False(tracker.Called);
        Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task TunnelHost_WhenConnected_CallsNext()
    {
        var (middleware, context, tracker, _) = CreateMiddleware(
            tunnelUrl: "https://tunnel.example.com",
            tunnelConnected: true);
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("tunnel.example.com", 443);
        context.Request.QueryString = new QueryString($"?path={TendrilHome}/test/image.png");

        await middleware.InvokeAsync(context);

        Assert.True(tracker.Called);
    }

    [Fact]
    public async Task TunnelHost_WhenNotConnected_Returns403()
    {
        var (middleware, context, tracker, _) = CreateMiddleware(
            tunnelUrl: "https://tunnel.example.com",
            tunnelConnected: false);
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("tunnel.example.com", 443);
        context.Request.QueryString = new QueryString($"?path={TendrilHome}/test/image.png");

        await middleware.InvokeAsync(context);

        Assert.False(tracker.Called);
        Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task AllowedHostsConfig_CallsNext()
    {
        var settings = new TendrilSettings
        {
            Security = new SecuritySettings
            {
                AllowedHosts = new List<string> { "custom.local" }
            }
        };
        var (middleware, context, tracker, _) = CreateMiddleware(settings);
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("custom.local", 5000);
        context.Request.QueryString = new QueryString($"?path={TendrilHome}/test/image.png");

        await middleware.InvokeAsync(context);

        Assert.True(tracker.Called);
    }

    [Fact]
    public async Task CrossOrigin_Returns403()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.Headers["Origin"] = "https://evil.example.com";
        context.Request.QueryString = new QueryString($"?path={TendrilHome}/test/image.png");

        await middleware.InvokeAsync(context);

        Assert.False(tracker.Called);
        Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task MatchingOrigin_CallsNext()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.Headers["Origin"] = "http://localhost:5000";
        context.Request.QueryString = new QueryString($"?path={TendrilHome}/test/image.png");

        await middleware.InvokeAsync(context);

        Assert.True(tracker.Called);
    }

    [Fact]
    public async Task CrossSiteFetch_Returns403()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.Headers["Sec-Fetch-Site"] = "cross-site";
        context.Request.QueryString = new QueryString($"?path={TendrilHome}/test/image.png");

        await middleware.InvokeAsync(context);

        Assert.False(tracker.Called);
        Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task SameOriginFetch_CallsNext()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.Headers["Sec-Fetch-Site"] = "same-origin";
        context.Request.QueryString = new QueryString($"?path={TendrilHome}/test/image.png");

        await middleware.InvokeAsync(context);

        Assert.True(tracker.Called);
    }

    [Fact]
    public async Task SameSiteFetch_CallsNext()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.Headers["Sec-Fetch-Site"] = "same-site";
        context.Request.QueryString = new QueryString($"?path={TendrilHome}/test/image.png");

        await middleware.InvokeAsync(context);

        Assert.True(tracker.Called);
    }

    [Fact]
    public async Task NoneFetch_CallsNext()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.Headers["Sec-Fetch-Site"] = "none";
        context.Request.QueryString = new QueryString($"?path={TendrilHome}/test/image.png");

        await middleware.InvokeAsync(context);

        Assert.True(tracker.Called);
    }

    [Fact]
    public async Task AbsentFetchSite_CallsNext()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.QueryString = new QueryString($"?path={TendrilHome}/test/image.png");

        await middleware.InvokeAsync(context);

        Assert.True(tracker.Called);
    }

    [Fact]
    public async Task SshKey_Returns404()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.QueryString = new QueryString("?path=C:/Users/x/.ssh/id_rsa");

        await middleware.InvokeAsync(context);

        Assert.False(tracker.Called);
        Assert.Equal(404, context.Response.StatusCode);
    }

    [Fact]
    public async Task YamlFile_Returns404()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.QueryString = new QueryString("?path=C:/config/config.yaml");

        await middleware.InvokeAsync(context);

        Assert.False(tracker.Called);
        Assert.Equal(404, context.Response.StatusCode);
    }

    [Fact]
    public async Task JsonFile_Returns404()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.QueryString = new QueryString("?path=C:/secrets/.credentials.json");

        await middleware.InvokeAsync(context);

        Assert.False(tracker.Called);
        Assert.Equal(404, context.Response.StatusCode);
    }

    [Fact]
    public async Task MarkdownFile_Returns404()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.QueryString = new QueryString("?path=C:/docs/README.md");

        await middleware.InvokeAsync(context);

        Assert.False(tracker.Called);
        Assert.Equal(404, context.Response.StatusCode);
    }

    [Fact]
    public async Task CSharpFile_Returns404()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.QueryString = new QueryString("?path=C:/src/Program.cs");

        await middleware.InvokeAsync(context);

        Assert.False(tracker.Called);
        Assert.Equal(404, context.Response.StatusCode);
    }

    [Fact]
    public async Task ExtensionlessFile_Returns404()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.QueryString = new QueryString("?path=C:/etc/hosts");

        await middleware.InvokeAsync(context);

        Assert.False(tracker.Called);
        Assert.Equal(404, context.Response.StatusCode);
    }

    [Fact]
    public async Task PngFile_CallsNext()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.QueryString = new QueryString($"?path={TendrilHome}/images/test.png");

        await middleware.InvokeAsync(context);

        Assert.True(tracker.Called);
    }

    [Fact]
    public async Task JpgFile_CallsNext()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.QueryString = new QueryString($"?path={TendrilHome}/images/test.jpg");

        await middleware.InvokeAsync(context);

        Assert.True(tracker.Called);
    }

    [Fact]
    public async Task SvgFile_CallsNext()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.QueryString = new QueryString($"?path={TendrilHome}/images/test.svg");

        await middleware.InvokeAsync(context);

        Assert.True(tracker.Called);
    }

    [Fact]
    public async Task WebpFile_CallsNext()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.QueryString = new QueryString($"?path={TendrilHome}/images/test.webp");

        await middleware.InvokeAsync(context);

        Assert.True(tracker.Called);
    }

    [Fact]
    public async Task AvifFile_CallsNext()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.QueryString = new QueryString($"?path={TendrilHome}/images/test.avif");

        await middleware.InvokeAsync(context);

        Assert.True(tracker.Called);
    }

    [Fact]
    public async Task PdfFile_CallsNext()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.QueryString = new QueryString($"?path={TendrilHome}/docs/manual.pdf");

        await middleware.InvokeAsync(context);

        Assert.True(tracker.Called);
    }

    [Fact]
    public async Task TraversalPath_Returns404()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.QueryString = new QueryString("?path=D:/.tendril/Plans/../../Users/x/.ssh/id_rsa");

        await middleware.InvokeAsync(context);

        Assert.False(tracker.Called);
        Assert.Equal(404, context.Response.StatusCode);
    }

    [Fact]
    public async Task AllowedRequest_SetsSecurityHeaders()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.QueryString = new QueryString($"?path={TendrilHome}/test/image.png");

        await middleware.InvokeAsync(context);

        Assert.True(tracker.Called);
        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.Equal("default-src 'none'; sandbox", context.Response.Headers["Content-Security-Policy"].ToString());
    }

    [Fact]
    public async Task PrivateIpv4_10Network_CallsNext()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("10.0.0.5", 5000);
        context.Request.QueryString = new QueryString($"?path={TendrilHome}/test/image.png");

        await middleware.InvokeAsync(context);

        Assert.True(tracker.Called);
    }

    [Fact]
    public async Task PrivateIpv4_172Network_CallsNext()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("172.16.0.1", 5000);
        context.Request.QueryString = new QueryString($"?path={TendrilHome}/test/image.png");

        await middleware.InvokeAsync(context);

        Assert.True(tracker.Called);
    }

    [Fact]
    public async Task PrivateIpv4_192Network_CallsNext()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("192.168.1.100", 5000);
        context.Request.QueryString = new QueryString($"?path={TendrilHome}/test/image.png");

        await middleware.InvokeAsync(context);

        Assert.True(tracker.Called);
    }

    [Fact]
    public async Task LocalMdnsHost_CallsNext()
    {
        var (middleware, context, tracker, _) = CreateMiddleware();
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("mycomputer.local", 5000);
        context.Request.QueryString = new QueryString($"?path={TendrilHome}/test/image.png");

        await middleware.InvokeAsync(context);

        Assert.True(tracker.Called);
    }

    [Fact]
    public async Task PngUnderTendrilHome_CallsNext()
    {
        var home = CreateTempDir();
        var (middleware, context, tracker, _) = CreateMiddleware(tendrilHome: home);
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.QueryString = new QueryString($"?path={Path.Combine(home, "image.png")}");

        await middleware.InvokeAsync(context);

        Assert.True(tracker.Called);
    }

    [Fact]
    public async Task PngOutsideEveryRoot_Returns404NotFound()
    {
        var home = CreateTempDir();
        var outside = CreateTempDir();
        var (middleware, context, tracker, _) = CreateMiddleware(tendrilHome: home);
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.QueryString = new QueryString($"?path={Path.Combine(outside, "image.png")}");

        await middleware.InvokeAsync(context);

        Assert.False(tracker.Called);
        Assert.Equal(404, context.Response.StatusCode);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = new StreamReader(context.Response.Body).ReadToEnd();
        Assert.Contains("File not found", body);
    }

    [Fact]
    public async Task PathUnderSecurityLocalFileRoot_CallsNext()
    {
        var home = CreateTempDir();
        var extraRoot = CreateTempDir();
        var settings = new TendrilSettings
        {
            Security = new SecuritySettings { LocalFileRoots = new List<string> { extraRoot } }
        };
        var (middleware, context, tracker, _) = CreateMiddleware(settings, tendrilHome: home);
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.QueryString = new QueryString($"?path={Path.Combine(extraRoot, "image.png")}");

        await middleware.InvokeAsync(context);

        Assert.True(tracker.Called);
    }

    [Fact]
    public async Task EditingSecurityLocalFileRoots_InvalidatesCache()
    {
        var home = CreateTempDir();
        var extraRoot = CreateTempDir();
        var (middleware, context, tracker, config) = CreateMiddleware(tendrilHome: home);
        var candidatePath = Path.Combine(extraRoot, "image.png");

        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.QueryString = new QueryString($"?path={candidatePath}");
        await middleware.InvokeAsync(context);
        Assert.False(tracker.Called);
        Assert.Equal(404, context.Response.StatusCode);

        config.Settings.Security = new SecuritySettings { LocalFileRoots = new List<string> { extraRoot } };
        config.SaveSettings();

        var context2 = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        context2.Request.Path = "/ivy/local-file";
        context2.Request.Host = new HostString("localhost", 5000);
        context2.Request.QueryString = new QueryString($"?path={candidatePath}");
        await middleware.InvokeAsync(context2);

        Assert.True(tracker.Called);
    }

    [Fact]
    public async Task PngViaSymlinkedDirectoryOutsideRoots_Returns404()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var home = CreateTempDir();
        var outside = CreateTempDir();
        var sharedLink = Path.Combine(home, "shared");
        Directory.CreateSymbolicLink(sharedLink, outside);

        var (middleware, context, tracker, _) = CreateMiddleware(tendrilHome: home);
        context.Request.Path = "/ivy/local-file";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.QueryString = new QueryString($"?path={Path.Combine(sharedLink, "photo.png")}");

        await middleware.InvokeAsync(context);

        Assert.False(tracker.Called);
        Assert.Equal(404, context.Response.StatusCode);
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tendril-lfgm-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
