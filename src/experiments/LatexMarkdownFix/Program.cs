using Ivy;

var server = new Server();
server.UseCulture("en-US");
#if DEBUG
server.UseHotReload();
#endif
server.AddAppsFromAssembly();
var appShellSettings = new AppShellSettings()
    .UseTabs(preventDuplicates: true);
server.UseAppShell(appShellSettings);
await server.RunAsync();
