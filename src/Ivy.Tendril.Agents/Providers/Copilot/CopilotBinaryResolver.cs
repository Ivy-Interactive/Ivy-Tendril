using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Providers.Copilot;

/// <summary>
/// Resolves the copilot binary: prefers standalone "copilot", falls back to "gh copilot".
/// </summary>
internal static class CopilotBinaryResolver
{
    private static (string FileName, string[] PrefixArgs)? _cached;

    public static (string FileName, string[] PrefixArgs) Resolve()
    {
        return _cached ??= ResolveCore();
    }

    private static (string FileName, string[] PrefixArgs) ResolveCore()
    {
        if (BinaryResolver.FindOnPath("copilot") is not null)
            return ("copilot", []);

        if (BinaryResolver.FindOnPath("gh") is not null)
            return ("gh", ["copilot"]);

        return ("copilot", []);
    }
}
