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
        "--enable-dev-tools", "--describe-connection", "--test-connection"
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

    // Suggests the closest known command/flag for an unrecognized token, e.g. "plna" -> "plan".
    public static string? SuggestCommand(string token)
    {
        token = token.Trim();
        if (token.Length == 0)
            return null;

        var candidates = token.StartsWith('-')
            ? ServerFlags.Concat(["--help", "--version"])
            : TopLevelCommands.Concat(LegacyCliCommands);

        var lowerToken = token.ToLowerInvariant();
        var maxDistance = token.Length <= 4 ? 1 : 2;

        string? best = null;
        var bestDistance = int.MaxValue;

        foreach (var candidate in candidates)
        {
            var distance = OptimalStringAlignmentDistance(lowerToken, candidate.ToLowerInvariant());
            if (distance > maxDistance)
                continue;

            if (best is null
                || distance < bestDistance
                || (distance == bestDistance && candidate.Length < best.Length)
                || (distance == bestDistance && candidate.Length == best.Length && string.CompareOrdinal(candidate, best) < 0))
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    // Optimal string alignment distance: Levenshtein plus adjacent-transposition swaps costing 1,
    // so a single-character swap like "plna" -> "plan" scores 1 instead of 2.
    private static int OptimalStringAlignmentDistance(string a, string b)
    {
        var lenA = a.Length;
        var lenB = b.Length;
        var d = new int[lenA + 1, lenB + 1];

        for (var i = 0; i <= lenA; i++)
            d[i, 0] = i;
        for (var j = 0; j <= lenB; j++)
            d[0, j] = j;

        for (var i = 1; i <= lenA; i++)
        {
            for (var j = 1; j <= lenB; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);

                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        }

        return d[lenA, lenB];
    }
}
