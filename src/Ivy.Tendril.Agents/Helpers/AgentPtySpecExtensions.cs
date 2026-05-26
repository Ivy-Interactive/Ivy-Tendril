using System.Runtime.InteropServices;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Helpers;

public static class AgentPtySpecExtensions
{
    public static AgentPtySpec ResolveCommand(this AgentPtySpec spec)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return spec;
        if (spec.CommandLine.Count == 0)
            return spec;

        var command = spec.CommandLine[0];
        if (Path.HasExtension(command) || Path.IsPathRooted(command))
            return spec;

        var resolved = BinaryResolver.FindOnPath(command);
        if (resolved is null)
            return spec;

        var newCommandLine = spec.CommandLine.ToList();
        newCommandLine[0] = resolved;
        return spec with { CommandLine = newCommandLine };
    }
}
