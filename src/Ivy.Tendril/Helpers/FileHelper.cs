using System.Globalization;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Helpers;

/// <summary>
///     File I/O helpers that use FileShare.ReadWrite and retry on transient lock errors.
///     Prevents "file being used by another process" errors when multiple threads or
///     processes access the same file concurrently (e.g. plan.yaml, costs.csv).
/// </summary>
internal static class FileHelper
{
    private const int MaxRetries = 5;

    private static readonly Regex CompletedTimestampRegex =
        new(@"\*\*Completed:\*\*\s*(.+)", RegexOptions.Compiled);

    private static readonly int[] RetryDelaysMs = [50, 150, 350, 750, 1500];

    /// <summary>
    ///     Checks if the given path is a symbolic link.
    ///     On Windows: checks FileAttributes.ReparsePoint.
    ///     On Linux/macOS: checks FileInfo.LinkTarget.
    /// </summary>
    private static bool IsSymbolicLink(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var attrs = File.GetAttributes(path);
                return (attrs & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            }

            var fileInfo = new FileInfo(path);
            return fileInfo.LinkTarget != null;
        }
        catch
        {
            // If we can't determine, assume it's not a symlink and let the file operation
            // fail with its natural error
            return false;
        }
    }

    /// <summary>
    ///     Validates that a path is fully qualified and not a symbolic link.
    ///     Throws ArgumentException for relative paths.
    ///     Throws UnauthorizedAccessException for symbolic links.
    /// </summary>
    private static void ValidatePath(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException($"Path must be fully qualified: {path}", nameof(path));

        if (IsSymbolicLink(path))
            throw new UnauthorizedAccessException($"Refusing to operate on symbolic link: {path}");
    }

    /// <summary>
    ///     Extracts the "**Completed:** &lt;timestamp&gt;" value from a log file.
    ///     Returns null if the file doesn't exist, can't be read, or has no completed timestamp.
    /// </summary>
    public static DateTime? ExtractCompletedTimestamp(string logFilePath)
    {
        try
        {
            foreach (var line in ReadAllLines(logFilePath))
            {
                var match = CompletedTimestampRegex.Match(line);
                if (match.Success && DateTime.TryParse(match.Groups[1].Value.Trim(),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal, out var dt))
                    return dt;
            }
        }
        catch
        {
            /* Best-effort: file may be locked or missing */
        }

        return null;
    }

    /// <summary>
    ///     Reads all text from a file with retry logic for transient IO errors.
    ///     Callers should check <see cref="File.Exists(string)"/> before calling unless
    ///     the path comes from <c>Directory.GetFiles</c>/<c>EnumerateFiles</c> or an
    ///     explicit try-catch handles <see cref="FileNotFoundException"/>.
    /// </summary>
    public static string ReadAllText(string path)
    {
        ValidatePath(path);
        for (var attempt = 0; ; attempt++)
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < MaxRetries)
            {
                Thread.Sleep(RetryDelaysMs[attempt]);
            }
    }

    public static string[] ReadAllLines(string path)
    {
        ValidatePath(path);
        for (var attempt = 0; ; attempt++)
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                var lines = new List<string>();
                while (reader.ReadLine() is { } line)
                    lines.Add(line);
                return lines.ToArray();
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < MaxRetries)
            {
                Thread.Sleep(RetryDelaysMs[attempt]);
            }
    }

    public static void WriteAllText(string path, string contents)
    {
        ValidatePath(path);
        ClearReadOnly(path);
        for (var attempt = 0; ; attempt++)
            try
            {
                using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream);
                writer.Write(contents);
                return;
            }
            catch (UnauthorizedAccessException) when (attempt < MaxRetries)
            {
                if (attempt == 0) TryGrantCurrentUserAccess(path);
                Thread.Sleep(RetryDelaysMs[attempt]);
            }
            catch (IOException) when (attempt < MaxRetries)
            {
                Thread.Sleep(RetryDelaysMs[attempt]);
            }
    }

    /// <inheritdoc cref="ReadAllText"/>
    public static async Task<string> ReadAllTextAsync(string path)
    {
        ValidatePath(path);
        for (var attempt = 0; ; attempt++)
            try
            {
                var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                await using (stream.ConfigureAwait(false))
                {
                    using var reader = new StreamReader(stream);
                    return await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < MaxRetries)
            {
                await Task.Delay(RetryDelaysMs[attempt]).ConfigureAwait(false);
            }
    }

    public static async Task WriteAllTextAsync(string path, string contents)
    {
        ValidatePath(path);
        ClearReadOnly(path);
        for (var attempt = 0; ; attempt++)
            try
            {
                var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                await using (stream.ConfigureAwait(false))
                {
                    await using var writer = new StreamWriter(stream);
                    await writer.WriteAsync(contents).ConfigureAwait(false);
                    return;
                }
            }
            catch (UnauthorizedAccessException) when (attempt < MaxRetries)
            {
                if (attempt == 0) TryGrantCurrentUserAccess(path);
                await Task.Delay(RetryDelaysMs[attempt]).ConfigureAwait(false);
            }
            catch (IOException) when (attempt < MaxRetries)
            {
                await Task.Delay(RetryDelaysMs[attempt]).ConfigureAwait(false);
            }
    }

    /// <summary>
    ///     Streams lines from a file one at a time without loading the entire file into memory.
    ///     Uses the same FileShare.ReadWrite and retry semantics as ReadAllLines.
    /// </summary>
    public static IEnumerable<string> EnumerateLines(string path)
    {
        ValidatePath(path);
        FileStream? stream = null;
        for (var attempt = 0; ; attempt++)
            try
            {
                stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                break;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < MaxRetries)
            {
                Thread.Sleep(RetryDelaysMs[attempt]);
            }

        using (stream)
        using (var reader = new StreamReader(stream!))
        {
            while (reader.ReadLine() is { } line)
                yield return line;
        }
    }

    public static void AppendAllText(string path, string contents)
    {
        ValidatePath(path);
        ClearReadOnly(path);
        for (var attempt = 0; ; attempt++)
            try
            {
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream);
                writer.Write(contents);
                return;
            }
            catch (UnauthorizedAccessException) when (attempt < MaxRetries)
            {
                if (attempt == 0) TryGrantCurrentUserAccess(path);
                Thread.Sleep(RetryDelaysMs[attempt]);
            }
            catch (IOException) when (attempt < MaxRetries)
            {
                Thread.Sleep(RetryDelaysMs[attempt]);
            }
    }

    private static void ClearReadOnly(string path)
    {
        if (!File.Exists(path)) return;
        var attrs = File.GetAttributes(path);
        if ((attrs & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
    }

    /// <summary>
    ///     Creates a directory and ensures the current user has write access.
    ///     Handles the case where an existing directory was created by an elevated process.
    /// </summary>
    public static void EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var testFile = Path.Combine(path, ".tendril-write-test");
            using (File.Create(testFile)) { }
            File.Delete(testFile);
        }
        catch (UnauthorizedAccessException)
        {
            TryGrantCurrentUserAccess(path);
        }
    }

    /// <summary>
    ///     Attempts to grant the current user full control on a file or directory.
    ///     This handles the case where a plan folder was created by an elevated process
    ///     (e.g. Tendril running as Administrator) and the current non-elevated user
    ///     only has read access via BUILTIN\Users.
    /// </summary>
    private static void TryGrantCurrentUserAccess(string path, ILogger? logger = null)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var identity = WindowsIdentity.GetCurrent();
            var user = identity.User;
            if (user == null) return;

            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                var security = info.GetAccessControl();
                security.AddAccessRule(new FileSystemAccessRule(
                    user, FileSystemRights.FullControl, AccessControlType.Allow));
                info.SetAccessControl(security);
            }
            else if (Directory.Exists(path))
            {
                var info = new DirectoryInfo(path);
                var security = info.GetAccessControl();
                security.AddAccessRule(new FileSystemAccessRule(
                    user, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
                info.SetAccessControl(security);
            }
        }
        catch (Exception ex)
        {
            // Best-effort: if we can't fix the ACL (e.g. not owner), the caller will
            // get the original UnauthorizedAccessException on the next retry.
            var message = $"Failed to grant access to {path}: {ex.Message}";
            if (logger != null)
                logger.LogWarning(ex, "Failed to grant access to {Path}", path);
            else
                Console.Error.WriteLine(message);
        }
    }
}