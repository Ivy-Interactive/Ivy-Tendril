using Ivy;

var server = new Server();
server.UseAppShell();
server.AddApp<PreBufferedDemo>();
server.AddApp<LiveStreamDemo>();
server.AddApp<ErrorDemo>();
server.AddApp<TableOutputDemo>();
server.AddApp<TendrilProcessDemo>();
server.AddApp<DraftMarkdownDemo>();
await server.RunAsync();
