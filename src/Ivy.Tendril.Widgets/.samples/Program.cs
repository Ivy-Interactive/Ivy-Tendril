using Ivy;
using Ivy.Tendril.Widgets;
using Microsoft.Extensions.DependencyInjection;
using WidgetSamples.Apps.WebViewer;

var server = new Server();
server.UseAppShell();
// WebViewer proxy/capture/service-worker endpoints, hosted on the same origin as the app.
server.ReservePaths(WebViewerProxy.ReservedPaths);
// A small site of our own for the WebViewer samples to point at: deterministic pages with
// navigation, forms, a long URL, a slow page and a 404, served without touching the network.
server.ReservePaths(DemoSite.Prefix);
server.Services.AddSingleton(new DemoSiteAddress($"http://localhost:{server.Args.Port}{DemoSite.Prefix}"));
server.UseWebApplication(app =>
{
    app.MapWebViewerProxy();
    app.MapDemoSite();
});
server.AddAppsFromAssembly();
server.DangerouslyAllowLocalFiles();
await server.RunAsync();
