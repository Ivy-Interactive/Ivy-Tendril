using System.Text.RegularExpressions;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Helpers;

/// <summary>
///     Recreates a project's environment files inside a worktree. A git worktree is a clean checkout,
///     so the untracked <c>.env</c> files the original repo relies on are absent and services and
///     database migrations fail to boot. Each <see cref="ProjectEnvFileConfig" /> is rebuilt from its
///     template plus overrides, with placeholders resolved against the plan's allocated ports so
///     inter-service URLs (<c>VITE_API_URL</c>, <c>DB_SERVICE_URL</c>) point at the right ports.
/// </summary>
public static class EnvironmentMaterializationHelper
{
    /// <summary><c>${ports.&lt;name&gt;}</c> — the port assigned to a named service.</summary>
    private static readonly Regex PortPlaceholder = new(@"\$\{ports\.([A-Za-z0-9_.-]+)\}", RegexOptions.Compiled);

    /// <summary><c>${env.&lt;VAR&gt;}</c> — a host environment variable, empty when unset.</summary>
    private static readonly Regex EnvPlaceholder = new(@"\$\{env\.([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    /// <summary>A <c>KEY=VALUE</c> line, optionally prefixed with <c>export </c>.</summary>
    private static readonly Regex EnvAssignment = new(@"^(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=(.*)$", RegexOptions.Compiled);

    /// <summary>
    ///     Writes every environment file the project configures into <paramref name="worktreeRoot" /> and
    ///     returns the number of files written.
    /// </summary>
    public static int MaterializeEnvFiles(
        ProjectConfig project,
        IReadOnlyDictionary<string, int> allocatedPorts,
        string worktreeRoot,
        string? tendrilHome = null)
    {
        var written = 0;
        foreach (var envFile in project.EnvFiles)
        {
            if (string.IsNullOrWhiteSpace(envFile.Path)) continue;

            var targetPath = Path.GetFullPath(Path.Combine(worktreeRoot, envFile.Path));
            var content = BuildEnvFileContent(envFile, project, allocatedPorts, worktreeRoot, tendrilHome);

            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir))
                Directory.CreateDirectory(targetDir);

            // Content is joined with "\n" unconditionally: dotenv parsers in Node and Python treat a
            // trailing CR as part of the value, which silently corrupts URLs and ports on Windows hosts.
            FileHelper.WriteAllText(targetPath, content);
            written++;
        }

        return written;
    }

    /// <summary>
    ///     Renders one environment file: the template's lines with placeholders expanded (comments and
    ///     blank lines preserved), then <see cref="ProjectEnvFileConfig.Overrides" /> applied — replacing
    ///     a key the template already defines in place, appending the rest.
    /// </summary>
    public static string BuildEnvFileContent(
        ProjectEnvFileConfig envFile,
        ProjectConfig project,
        IReadOnlyDictionary<string, int> allocatedPorts,
        string worktreeRoot,
        string? tendrilHome = null)
    {
        string Expand(string value) => ExpandPlaceholders(value, allocatedPorts, project.Ports, tendrilHome);

        var lines = new List<string>();
        var keyLineIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(envFile.Template))
        {
            var templatePath = Path.GetFullPath(Path.Combine(worktreeRoot, envFile.Template));
            if (File.Exists(templatePath))
                foreach (var rawLine in FileHelper.ReadAllLines(templatePath))
                {
                    var match = EnvAssignment.Match(rawLine.Trim());
                    if (!match.Success)
                    {
                        lines.Add(rawLine.TrimEnd('\r'));
                        continue;
                    }

                    var key = match.Groups[1].Value;
                    keyLineIndex[key] = lines.Count;
                    lines.Add($"{key}={Expand(match.Groups[2].Value.Trim())}");
                }
        }

        foreach (var (key, rawValue) in envFile.Overrides)
        {
            var line = $"{key}={Expand(rawValue ?? "")}";
            if (keyLineIndex.TryGetValue(key, out var index))
                lines[index] = line;
            else
            {
                keyLineIndex[key] = lines.Count;
                lines.Add(line);
            }
        }

        return lines.Count == 0 ? "" : string.Join("\n", lines) + "\n";
    }

    /// <summary>
    ///     Resolves <c>${ports.&lt;name&gt;}</c>, <c>${env.&lt;VAR&gt;}</c> and <c>%VAR%</c> in one value.
    ///     A port name resolves to its allocated port, falling back to the project's configured default;
    ///     an unknown name is left as written so the misconfiguration is visible in the generated file
    ///     rather than becoming an empty string. <c>%VAR%</c> is delegated to
    ///     <see cref="VariableExpansion.ExpandVariables" /> with path normalization off, which would
    ///     otherwise rewrite the separators of URLs and Windows paths held in env values.
    /// </summary>
    internal static string ExpandPlaceholders(
        string value,
        IReadOnlyDictionary<string, int> allocatedPorts,
        IReadOnlyDictionary<string, ProjectPortConfig> portDefaults,
        string? tendrilHome = null)
    {
        if (string.IsNullOrEmpty(value)) return value;

        var expanded = ExpandPortPlaceholders(value, allocatedPorts, portDefaults);

        expanded = EnvPlaceholder.Replace(expanded, match =>
            Environment.GetEnvironmentVariable(match.Groups[1].Value) ?? "");

        return VariableExpansion.ExpandVariables(expanded, tendrilHome, normalizePaths: false);
    }

    /// <summary>
    ///     Replaces every <c>${ports.&lt;name&gt;}</c> with the port allocated to that name, falling back to
    ///     the project's configured default. Shared with review action command lines, which need the same
    ///     placeholder but none of the env-file expansions.
    /// </summary>
    public static string ExpandPortPlaceholders(
        string value,
        IReadOnlyDictionary<string, int> allocatedPorts,
        IReadOnlyDictionary<string, ProjectPortConfig>? portDefaults = null)
    {
        if (string.IsNullOrEmpty(value)) return value;

        return PortPlaceholder.Replace(value, match =>
        {
            var name = match.Groups[1].Value;
            if (allocatedPorts.TryGetValue(name, out var port))
                return port.ToString();
            if (portDefaults != null && portDefaults.TryGetValue(name, out var config) && config.DefaultPort > 0)
                return config.DefaultPort.ToString();
            return match.Value;
        });
    }

    /// <summary>
    ///     The key/value pairs an environment file resolves to, without writing it. Used by
    ///     <c>tendril plan env get</c> to show what a worktree would receive.
    /// </summary>
    public static List<KeyValuePair<string, string>> ResolveEnvValues(
        ProjectEnvFileConfig envFile,
        ProjectConfig project,
        IReadOnlyDictionary<string, int> allocatedPorts,
        string worktreeRoot,
        string? tendrilHome = null)
    {
        var content = BuildEnvFileContent(envFile, project, allocatedPorts, worktreeRoot, tendrilHome);
        var values = new List<KeyValuePair<string, string>>();

        foreach (var line in content.Split('\n'))
        {
            var match = EnvAssignment.Match(line.Trim());
            if (match.Success)
                values.Add(new KeyValuePair<string, string>(match.Groups[1].Value, match.Groups[2].Value));
        }

        return values;
    }
}
