using Ivy;
using WidgetSamples;

var server = new Server();
server.UseAppShell();
// WebViewer proxy/capture/service-worker endpoints (same origin as the app).
server.ReservePaths("/__proxy", "/__view", "/__capture", "/__captures", "/__lib", "/sw.js");
server.UseWebApplication(app => app.MapWebViewerProxy(System.AppContext.BaseDirectory));
server.AddAppsFromAssembly();
server.DangerouslyAllowLocalFiles();
await server.RunAsync();
