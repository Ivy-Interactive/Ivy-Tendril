using Ivy;

var server = new Server();
server.UseAppShell();
server.AddAppsFromAssembly();
server.DangerouslyAllowLocalFiles();
await server.RunAsync();
