using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Ivy.Tendril.Services.Wireframes;
using Ivy.Tendril.Wireframe.Assets;
using Ivy.Tendril.Wireframe.Hosting;
using Ivy.Tendril.Wireframe.Project;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Test.Wireframe;

/// <summary>
/// Plan wireframes served live from the host's own origin. These run the real esbuild watcher
/// against scaffolded projects, mounted exactly as TendrilServer mounts them.
/// </summary>
public class WireframeHostTests : IAsyncLifetime
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "wireframe-host-tests", Guid.NewGuid().ToString("N")[..8]);

    private WebApplication _app = null!;
    private WireframeHost _host = null!;
    private HttpClient _http = null!;
    private string _url = "";

    public async Task InitializeAsync()
    {
        Scaffold("1", "alpha");
        Scaffold("2", "beta");

        _host = new WireframeHost(
            AssetCatalog.Default,
            (scope, name) => Path.Combine(_root, scope, name),
            new WireframeHostOptions
            {
                DisconnectGrace = TimeSpan.FromMilliseconds(200),
                UnwatchedGrace = TimeSpan.FromSeconds(30),
            });

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));

        _app = builder.Build();
        _app.UseWebSockets();
        _app.MapWireframePayload(AssetCatalog.Default);
        _app.MapWireframeSite(WireframeHost.RoutePattern, AssetCatalog.Default, _host);
        await _app.StartAsync();

        _url = _app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(_url) };
    }

    public async Task DisposeAsync()
    {
        _http?.Dispose();
        if (_host is not null) await _host.DisposeAsync();
        if (_app is not null) await _app.DisposeAsync();
        WireframeTempRoot.Remove(_root);
    }

    private WireframeProject Scaffold(string scope, string name)
    {
        var project = WireframeProject.At(Path.Combine(_root, scope, name));
        new ProjectScaffolder(AssetCatalog.Default).Scaffold(project);
        return project;
    }

    private async Task<string> StatusAsync(string path)
    {
        var json = await _http.GetStringAsync($"{path}__wireframe/status");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("phase").GetString()!;
    }

    private Task<ClientWebSocket> ConnectAsync(string path) => ConnectAsync(path, new ClientWebSocket());

    private async Task<ClientWebSocket> ConnectAsync(string path, ClientWebSocket socket)
    {
        await socket.ConnectAsync(new Uri(_url.Replace("http://", "ws://") + path + "__wireframe/hmr"), CancellationToken.None);
        return socket;
    }

    [Fact]
    public async Task The_page_names_its_bundle_and_reload_client_under_its_own_address()
    {
        var html = await _http.GetStringAsync("/__wireframes/1/alpha/");

        Assert.Contains("src=\"/__wireframes/1/alpha/__wireframe/out/bundle.js\"", html);
        Assert.Contains("data-base=\"/__wireframes/1/alpha/\"", html);
        // The shared payload stays at one address for every wireframe.
        Assert.Contains("href=\"/__wireframe/css/tendril.css\"", html);

        var bundle = await _http.GetAsync("/__wireframes/1/alpha/__wireframe/out/bundle.js");
        Assert.Equal(HttpStatusCode.OK, bundle.StatusCode);
        Assert.StartsWith("text/javascript", bundle.Content.Headers.ContentType!.ToString());

        var vendor = await _http.GetAsync("/__wireframe/css/tendril.css");
        Assert.Equal(HttpStatusCode.OK, vendor.StatusCode);
    }

    [Fact]
    public async Task An_address_without_its_trailing_slash_is_redirected_so_relative_urls_resolve()
    {
        var response = await _http.GetAsync("/__wireframes/1/alpha");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.EndsWith("/__wireframes/1/alpha/", response.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData("/__wireframes/1/Alpha/")]
    [InlineData("/__wireframes/1/al_pha/")]
    [InlineData("/__wireframes/1/-alpha/")]
    [InlineData("/__wireframes/x1/alpha/")]
    [InlineData("/__wireframes/1/nope/")]
    public async Task Addresses_that_are_not_a_valid_existing_wireframe_are_not_found(string path)
    {
        var response = await _http.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_wireframe_that_does_not_exist_reports_missing()
    {
        Assert.Equal("missing", await StatusAsync("/__wireframes/1/nope/"));
        Assert.Equal(0, _host.RunningWatchers);
    }

    [Fact]
    public async Task Public_files_cannot_escape_the_project()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "1", "secret.txt"), "not yours");

        foreach (var path in new[]
                 {
                     "/__wireframes/1/alpha/..%2F..%2Fsecret.txt",
                     "/__wireframes/1/alpha/..%5C..%5Csecret.txt",
                 })
        {
            var response = await _http.GetAsync(path);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("not yours", body);
        }
    }

    [Fact]
    public async Task An_edit_on_disk_pushes_a_reload_to_the_open_page()
    {
        Assert.Equal("running", await StatusAsync("/__wireframes/1/alpha/"));
        using var socket = await ConnectAsync("/__wireframes/1/alpha/");

        var app = Path.Combine(_root, "1", "alpha", "src", "App.tsx");
        var source = await File.ReadAllTextAsync(app);
        await File.WriteAllTextAsync(app, source.Replace("<div />", "<div id=\"edited\" />", StringComparison.Ordinal));

        Assert.Contains("reload", await ReceiveAsync(socket, "reload", TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task Opening_another_plan_stops_the_previous_plans_watchers_and_tells_its_pages()
    {
        Assert.Equal("running", await StatusAsync("/__wireframes/1/alpha/"));
        using var socket = await ConnectAsync("/__wireframes/1/alpha/");
        Assert.Equal(1, _host.RunningWatchers);

        Assert.Equal("running", await StatusAsync("/__wireframes/2/beta/"));

        Assert.Equal("2", _host.ActiveScope);
        Assert.Equal(1, _host.RunningWatchers);
        Assert.Contains("stopped", await ReceiveAsync(socket, "stopped", TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task A_page_left_open_on_a_plan_that_is_no_longer_live_is_told_to_stop_listening()
    {
        Assert.Equal("running", await StatusAsync("/__wireframes/1/alpha/"));
        Assert.Equal("running", await StatusAsync("/__wireframes/2/beta/"));

        // Reconnecting would reload the old page and take the preview back from plan 2.
        using var socket = await ConnectAsync("/__wireframes/1/alpha/");
        Assert.Contains("stopped", await ReceiveAsync(socket, "stopped", TimeSpan.FromSeconds(10)));
        Assert.Equal("2", _host.ActiveScope);

        // It can still load the files it was built with.
        var bundle = await _http.GetAsync("/__wireframes/1/alpha/__wireframe/out/bundle.js");
        Assert.Equal(HttpStatusCode.OK, bundle.StatusCode);
    }

    [Fact]
    public async Task Closing_the_last_page_stops_the_watcher_but_keeps_serving_the_build()
    {
        Assert.Equal("running", await StatusAsync("/__wireframes/1/alpha/"));
        var socket = await ConnectAsync("/__wireframes/1/alpha/");
        Assert.Equal(1, _host.RunningWatchers);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "navigated away", CancellationToken.None);
        socket.Dispose();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (_host.RunningWatchers > 0 && DateTime.UtcNow < deadline) await Task.Delay(50);

        Assert.Equal(0, _host.RunningWatchers);
        var bundle = await _http.GetAsync("/__wireframes/1/alpha/__wireframe/out/bundle.js");
        Assert.Equal(HttpStatusCode.OK, bundle.StatusCode);
    }

    [Fact]
    public async Task Plan_ids_with_and_without_padding_are_the_same_plan()
    {
        Assert.Equal("running", await StatusAsync("/__wireframes/00001/alpha/"));
        Assert.Equal("running", await StatusAsync("/__wireframes/1/alpha/"));
        Assert.Equal("1", _host.ActiveScope);
        Assert.Equal(1, _host.RunningWatchers);
    }

    [Fact]
    public void Plan_wireframes_resolve_inside_the_plan_folder()
    {
        var plans = Path.Combine(_root, "plans");
        Directory.CreateDirectory(Path.Combine(plans, "00042-CheckoutRedesign"));

        Assert.Equal(
            Path.Combine(plans, "00042-CheckoutRedesign", "Wireframes", "payment"),
            PlanWireframes.ResolveRoot(plans, "42", "payment"));
        Assert.Null(PlanWireframes.ResolveRoot(plans, "43", "payment"));
        Assert.Null(PlanWireframes.ResolveRoot(plans, "42", "../escape"));
        Assert.Equal("/__wireframes/42/", PlanWireframes.BaseUrl(42));
    }

    /// <summary>Reads frames until one mentions <paramref name="expected"/>.</summary>
    private static async Task<string> ReceiveAsync(ClientWebSocket socket, string expected, TimeSpan patience)
    {
        var buffer = new byte[64 * 1024];
        var deadline = DateTime.UtcNow + patience;
        var seen = new StringBuilder();

        while (DateTime.UtcNow < deadline)
        {
            using var timeout = new CancellationTokenSource(deadline - DateTime.UtcNow);

            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(buffer, timeout.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or WebSocketException)
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close) break;

            var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
            seen.AppendLine(text);
            if (text.Contains(expected, StringComparison.Ordinal)) return text;
        }

        Assert.Fail($"No '{expected}' message arrived within {patience.TotalSeconds:0}s. " +
                    $"Frames seen: {(seen.Length == 0 ? "(none)" : seen.ToString())}");
        return string.Empty;
    }
}
