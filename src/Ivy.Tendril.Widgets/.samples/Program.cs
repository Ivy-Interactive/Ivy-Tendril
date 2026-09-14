using Ivy;
using Ivy.Tendril.Widgets;
using Ivy.Tendril.Wireframe.Assets;
using Ivy.Tendril.Wireframe.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using WidgetSamples.Apps.DraftMarkdown;
using WidgetSamples.Apps.WebViewer;

var server = new Server();
server.UseAppShell();
// WebViewer proxy/capture/service-worker endpoints, hosted on the same origin as the app.
server.ReservePaths(WebViewerProxy.ReservedPaths);
// A small site of our own for the WebViewer samples to point at: deterministic pages with
// navigation, forms, a long URL, a slow page and a 404, served without touching the network.
server.ReservePaths(DemoSite.Prefix);
server.Services.AddSingleton(new DemoSiteAddress($"http://localhost:{server.Args.Port}{DemoSite.Prefix}"));
// Live wireframes for the DraftMarkdown Wireframes sample, mounted as Tendril mounts a plan's.
server.ReservePaths(WireframeEndpoints.ReservedPaths);
var wireframes = WireframeSamples.CreateHost();
server.UseWebApplication(app =>
{
    app.MapWebViewerProxy();
    app.MapDemoSite();
    app.UseWebSockets();
    app.MapWireframePayload(AssetCatalog.Default);
    app.MapWireframeSite(WireframeHost.RoutePattern, AssetCatalog.Default, wireframes);
});
server.AddAppsFromAssembly();
server.DangerouslyAllowLocalFiles();
await server.RunAsync();
