namespace Ivy.Tendril.Commands;

internal enum CliInvocationKind
{
    Help,
    Version,
    CliCommand,
    LegacyCliCommand,
    ServerLaunch,
    Unknown
}

internal static class CliDispatcher
{
    // Every top-level Spectre command/branch registered in Program.ConfigureCliCommands.
    // Single source of truth for dispatch — the drift-guard test in CliDispatcherTests asserts
    // every command Spectre's own --help output lists is present here, so a command registered
    // without a matching entry fails the build instead of silently falling through to ServerLaunch.
    internal static readonly string[] TopLevelCommands =
    [
        "doctor", "project-analyzer", "generate-certs", "run",
        "db-version", "db-migrate", "db-reset", "reset",
        "update-promptwares", "promptware", "version", "update", "report-bug",
        "job", "plan", "verification", "models", "agent-instructions",
        "trash", "project", "config"
    ];

    // Legacy handlers not yet migrated to Spectre.Console.Cli (HashPasswordCommand, McpCommand).
    internal static readonly string[] LegacyCliCommands = ["mcp", "hash-password"];

    private static readonly string[] HelpTokens = ["--help", "-h", "-?", "/?"];

    // Flags Ivy 1.3.8's Server.Args accepts on a bare server launch — kebab-case of the
    // Port/FindAvailablePort/IKillForThisPort/PathBase/EnableDevTools/DescribeConnection/
    // TestConnection backing fields, confirmed via `strings` against the installed Ivy.dll 1.3.8
    // during plan 00078's execution (no literal "--xxx" constants exist in the assembly; the
    // parser derives flag names from these property names at runtime).
    private static readonly string[] ServerFlags =
    [
        "--port", "--find-available-port", "--i-kill-for-this-port", "--path-base",
        "--enable-dev-tools", "--describe-connection", "--test-connection", "--browse"
    ];

    // Server flags above that consume the following token as their value.
    private static readonly string[] ServerFlagsWithValue = ["--port", "--path-base"];

    public static CliInvocationKind Classify(string[] filteredArgs)
    {
        if (filteredArgs.Length == 0)
            return CliInvocationKind.ServerLaunch;

        if (filteredArgs[0] == "help" || filteredArgs.Any(HelpTokens.Contains))
            return CliInvocationKind.Help;

        if (filteredArgs[0] == "--version")
            return CliInvocationKind.Version;

        if (TopLevelCommands.Contains(filteredArgs[0]))
            return CliInvocationKind.CliCommand;

        if (LegacyCliCommands.Contains(filteredArgs[0]))
            return CliInvocationKind.LegacyCliCommand;

        if (IsServerFlagArgList(filteredArgs))
            return CliInvocationKind.ServerLaunch;

        return CliInvocationKind.Unknown;
    }

    private static bool IsServerFlagArgList(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!ServerFlags.Contains(args[i]))
                return false;

            if (ServerFlagsWithValue.Contains(args[i]))
                i++; // skip the value token that follows this flag
        }
        return true;
    }
}
