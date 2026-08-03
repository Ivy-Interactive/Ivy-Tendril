using System.Diagnostics;
using System.Text;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Helpers;

public static class AgentProcessHelper
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static ProcessStartInfo ToPsi(AgentProcessSpec spec)
    {
        var psi = new ProcessStartInfo
        {
            FileName = spec.FileName,
            WorkingDirectory = spec.WorkingDirectory,
            RedirectStandardOutput = spec.RedirectStdout,
            RedirectStandardError = spec.RedirectStderr,
            RedirectStandardInput = spec.RedirectStdin,
            UseShellExecute = spec.UseShellExecute,
            CreateNoWindow = spec.CreateNoWindow,
        };

        foreach (var arg in spec.Arguments)
            psi.ArgumentList.Add(arg);

        foreach (var (key, value) in spec.Environment)
            psi.Environment[key] = value;

        // Without these, .NET falls back to GetConsoleOutputCP()/GetConsoleCP(). Tendril runs
        // console-less (CreateNoWindow, WinExe), so those return 0 and the streams get the ANSI
        // codepage (Windows-1252 here). Agent output is UTF-8, so an em dash arrives as three
        // characters, and on stdin every character outside CP1252 is replaced with "?" (#1798).
        if (spec.RedirectStdin)
            psi.StandardInputEncoding = Utf8NoBom;
        if (spec.RedirectStdout)
            psi.StandardOutputEncoding = Utf8NoBom;
        if (spec.RedirectStderr)
            psi.StandardErrorEncoding = Utf8NoBom;

        return psi;
    }

    public static void EnsureTendrilOnPath(ProcessStartInfo psi)
    {
        var shimDir = EnsureTendrilShimDir();
        if (shimDir != null)
            PrependToPath(psi, shimDir);
    }

    /// <summary>
    /// Returns a directory to prepend to PATH so a spawned process can invoke the <c>tendril</c>
    /// CLI: either the directory of the running tendril executable, or a temp dir containing
    /// generated shims that proxy to <c>dotnet exec Ivy.Tendril.dll</c> (used in dev, where no
    /// tendril binary is installed). Returns null if neither can be located.
    /// </summary>
    public static string? EnsureTendrilShimDir()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath) &&
            Path.GetFileNameWithoutExtension(processPath).Equals("tendril", StringComparison.OrdinalIgnoreCase))
            return Path.GetDirectoryName(processPath);

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var dllPath = Path.Combine(baseDir, "Ivy.Tendril.dll");
        if (!File.Exists(dllPath))
            return null;

        var shimDir = Path.Combine(Path.GetTempPath(), "tendril-shim");
        FileHelper.EnsureDirectory(shimDir);

        try
        {
            var cmdShim = Path.Combine(shimDir, "tendril.cmd");
            if (!File.Exists(cmdShim))
                File.WriteAllText(cmdShim, $"@dotnet exec \"{dllPath}\" %*\r\n");

            var bashShim = Path.Combine(shimDir, "tendril");
            if (!File.Exists(bashShim))
            {
                var bashDllPath = dllPath.Replace('\\', '/');
                File.WriteAllText(bashShim, $"#!/usr/bin/env bash\ndotnet exec '{bashDllPath}' \"$@\"\n");
            }
        }
        catch (IOException)
        {
            // Another concurrent job/thread is creating/writing the shims
        }

        return shimDir;
    }

    /// <summary>
    /// Augments a PTY/process environment dictionary so a spawned coding agent can invoke the
    /// <c>tendril</c> CLI (via shim) and resolve the active config/plans. Mirrors what
    /// <see cref="EnsureTendrilOnPath"/> plus the TENDRIL_* vars do for ProcessStartInfo-based
    /// launches, but for the dictionary handed to <c>UsePty</c>.
    /// </summary>
    public static void ApplyTendrilEnvironment(IDictionary<string, string> env, IConfigService config)
    {
        if (!string.IsNullOrEmpty(config.TendrilHome))
            env["TENDRIL_HOME"] = config.TendrilHome;
        env["TENDRIL_CONFIG"] = config.ConfigPath;
        env["TENDRIL_PLANS"] = config.PlanFolder;

        var shimDir = EnsureTendrilShimDir();
        if (shimDir != null)
        {
            var current = env.TryGetValue("PATH", out var p) && !string.IsNullOrEmpty(p)
                ? p
                : Environment.GetEnvironmentVariable("PATH");
            env["PATH"] = $"{shimDir}{Path.PathSeparator}{current}";
        }
    }

    public static void ResolveCommandShim(ProcessStartInfo psi)
    {
        if (!OperatingSystem.IsWindows()) return;

        var fileName = psi.FileName;
        if (Path.IsPathRooted(fileName) || Path.HasExtension(fileName)) return;

        var resolved = Agents.Helpers.BinaryResolver.FindOnPath(fileName);
        if (resolved != null)
        {
            psi.FileName = resolved;
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
