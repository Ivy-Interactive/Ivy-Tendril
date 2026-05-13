using System.Diagnostics;

namespace Ivy.Tendril.Helpers;

public static class AgentProcessHelper
{
    public static void EnsureTendrilOnPath(ProcessStartInfo psi)
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath) &&
            Path.GetFileNameWithoutExtension(processPath).Equals("tendril", StringComparison.OrdinalIgnoreCase))
        {
            PrependToPath(psi, Path.GetDirectoryName(processPath)!);
            return;
        }

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var dllPath = Path.Combine(baseDir, "Ivy.Tendril.dll");
        if (File.Exists(dllPath))
        {
            var shimDir = Path.Combine(Path.GetTempPath(), "tendril-shim");
            FileHelper.EnsureDirectory(shimDir);

            var cmdShim = Path.Combine(shimDir, "tendril.cmd");
            File.WriteAllText(cmdShim, $"@dotnet exec \"{dllPath}\" %*\r\n");

            var bashDllPath = dllPath.Replace('\\', '/');
            var bashShim = Path.Combine(shimDir, "tendril");
            File.WriteAllText(bashShim, $"#!/usr/bin/env bash\ndotnet exec '{bashDllPath}' \"$@\"\n");

            PrependToPath(psi, shimDir);
        }
    }

    public static void ResolveCommandShim(ProcessStartInfo psi)
    {
        if (!OperatingSystem.IsWindows()) return;

        var fileName = psi.FileName;
        if (Path.IsPathRooted(fileName) || Path.HasExtension(fileName)) return;

        var pathDirs = (psi.Environment.TryGetValue("PATH", out var p) ? p : Environment.GetEnvironmentVariable("PATH"))
            ?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];

        foreach (var dir in pathDirs)
        {
            var cmdPath = Path.Combine(dir, fileName + ".cmd");
            if (File.Exists(cmdPath))
            {
                psi.FileName = cmdPath;
                return;
            }
        }
    }

    public static string FormatCliCommand(ProcessStartInfo psi)
    {
        var parts = new List<string> { psi.FileName };
        parts.AddRange(psi.ArgumentList);
        return string.Join(" ", parts.Select(p => p.Contains(' ') ? $"\"{p}\"" : p));
    }

    private static void PrependToPath(ProcessStartInfo psi, string dir)
    {
        var current = psi.Environment.TryGetValue("PATH", out var p) ? p : Environment.GetEnvironmentVariable("PATH");
        psi.Environment["PATH"] = $"{dir}{Path.PathSeparator}{current}";
    }
}
