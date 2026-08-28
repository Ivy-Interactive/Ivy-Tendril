using Ivy;
using Ivy.Tendril.Widgets;

var server = new Server();
server.UseAppShell();
// WebViewer proxy/capture/service-worker endpoints, hosted on the same origin as the app.
server.ReservePaths(WebViewerProxy.ReservedPaths);
server.UseWebApplication(app => app.MapWebViewerProxy());
server.AddAppsFromAssembly();
server.DangerouslyAllowLocalFiles();
await server.RunAsync();
