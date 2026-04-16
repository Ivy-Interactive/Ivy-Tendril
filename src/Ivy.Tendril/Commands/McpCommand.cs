namespace Ivy.Tendril.Commands;

public static class McpCommand
{
    public static int Handle(string[] args)
    {
        if (args.Length == 0 || !args[0].Equals("mcp", StringComparison.OrdinalIgnoreCase))
            return -1;

        var server = new Mcp.TendrilMcpServer();
        return server.RunAsync(args.Skip(1).ToArray()).GetAwaiter().GetResult();
    }
}
