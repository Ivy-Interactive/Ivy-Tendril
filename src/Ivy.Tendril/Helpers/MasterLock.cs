using System.Diagnostics;
using System.Text.Json;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Helpers;

/// <summary>
///     Owns <c>TENDRIL_HOME/.master</c>: the atomic claim, the liveness test and the stale reclaim.
///     Every caller that used to carry its own copy of this logic (master election, JobService's
///     other-master check, the CLI's server discovery) goes through here so the copies cannot drift
///     apart again.
/// </summary>
/// <remarks>
///     The claim is taken with <see cref="FileMode.CreateNew" />, so exactly one of N racing
///     processes wins even when they start milliseconds apart. A claim is written before the port is
///     known (<c>port: 0</c> meaning "claimed, not yet bound") and gains its real port later via
///     <see cref="MasterLockHandle.Publish" />. A process that dies between the two leaves a claim
///     that the next launch reclaims, because its PID is dead or its heartbeat has expired.
/// </remarks>
public static class MasterLock
{
    /// <summary>A claim whose heartbeat is older than this is treated as abandoned.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(90);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    ///     The claim this process holds, if any. Set by <see cref="TryAcquire" /> so a claim taken in
    ///     <c>Program.Main</c> before anything is bound can be adopted by
    ///     <see cref="MasterElectionService" /> later in the same process instead of being re-taken.
    /// </summary>
    public static MasterLockHandle? Current { get; internal set; }

    public static string GetMasterFilePath(string tendrilHome) => Path.Combine(tendrilHome, ".master");

    /// <summary>
    ///     Atomically claims mastership, returning the handle on success and <c>null</c> when the file
    ///     already exists (whether the holder is alive or not: use <see cref="ReadLiveMaster" /> and
    ///     <see cref="TryReclaimStale" /> to decide what to do about it).
    /// </summary>
    public static MasterLockHandle? TryAcquire(string tendrilHome, ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(tendrilHome))
            return null;

        var path = GetMasterFilePath(tendrilHome);
        var now = DateTime.UtcNow;
        var data = new MasterElectionService.MasterFileData
        {
            Pid = Environment.ProcessId,
            Port = 0,
            Scheme = "http",
            StartedAt = now,
            Heartbeat = now
        };

        try
        {
            Directory.CreateDirectory(tendrilHome);

            // CreateNew is the whole point: it fails rather than truncating when a sibling got here
            // first, which is what makes two launches 15ms apart resolve to one master.
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(JsonSerializer.Serialize(data, JsonOptions));
            }
        }
        catch (IOException)
        {
            // Already claimed by someone (possibly dead) - the caller decides.
            return null;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to claim .master file at {Path}", path);
            return null;
        }

        var handle = new MasterLockHandle(path, data.Pid, now, logger);
        Current = handle;
        logger?.LogInformation("Claimed master lock (PID {Pid})", data.Pid);
        return handle;
    }

    /// <summary>
    ///     The recorded claim when it belongs to a live, other process: the file parses, its PID is
    ///     alive and is not us, and its heartbeat is within <see cref="StaleAfter" />. Otherwise
    ///     <c>null</c>, which means "no other master is running", not "no file".
    /// </summary>
    public static MasterElectionService.MasterFileData? ReadLiveMaster(string tendrilHome)
        => ReadLiveMaster(tendrilHome, out _);

    /// <summary>
    ///     <see cref="ReadLiveMaster(string)" /> plus the reason it rejected the claim, so callers can
    ///     log why they reclaimed rather than leaving a silent delete in the crash log.
    /// </summary>
    /// <param name="excludeSelf">
    ///     When <c>true</c> (the default, and what a launch guard wants) a claim naming this very
    ///     process is not "another master". Server discovery passes <c>false</c>, because an embedded
    ///     server talking to its own API is a legitimate case.
    /// </param>
    public static MasterElectionService.MasterFileData? ReadLiveMaster(
        string tendrilHome, out string? rejectReason, bool excludeSelf = true)
    {
        rejectReason = null;

        if (string.IsNullOrEmpty(tendrilHome))
        {
            rejectReason = "TENDRIL_HOME is not set";
            return null;
        }

        var path = GetMasterFilePath(tendrilHome);
        if (!File.Exists(path))
        {
            rejectReason = "no .master file";
            return null;
        }

        MasterElectionService.MasterFileData? data;
        try
        {
            data = JsonSerializer.Deserialize<MasterElectionService.MasterFileData>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex)
        {
            rejectReason = $"unreadable .master file ({ex.Message})";
            return null;
        }

        if (data == null)
        {
            rejectReason = "empty .master file";
            return null;
        }

        if (excludeSelf && data.Pid == Environment.ProcessId)
        {
            rejectReason = "claim belongs to this process";
            return null;
        }

        if (!IsProcessAlive(data.Pid))
        {
            rejectReason = $"PID {data.Pid} is dead";
            return null;
        }

        if (DateTime.UtcNow - data.Heartbeat > StaleAfter)
        {
            rejectReason = $"heartbeat is older than {StaleAfter.TotalSeconds:0}s";
            return null;
        }

        return data;
    }

    /// <summary>
    ///     Deletes a claim that <see cref="ReadLiveMaster" /> rejected. Returns <c>false</c> when the
    ///     claim turns out to be live after all, so a caller cannot evict a healthy master by racing.
    /// </summary>
    public static bool TryReclaimStale(string tendrilHome, ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(tendrilHome))
            return false;

        // excludeSelf: false - a claim held by this process is live, and deleting it here would be
        // this process quietly abandoning its own mastership.
        if (ReadLiveMaster(tendrilHome, out var reason, excludeSelf: false) != null)
            return false;

        var path = GetMasterFilePath(tendrilHome);
        if (!File.Exists(path))
            return true;

        try
        {
            File.Delete(path);
            logger?.LogInformation("Reclaimed stale .master file ({Reason})", reason);
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to delete stale .master file at {Path}", path);
            return false;
        }
    }

    internal static bool IsProcessAlive(int pid)
    {
        if (pid <= 0) return false;

        try
        {
            using var proc = Process.GetProcessById(pid);
            return !proc.HasExited;
        }
        catch
        {
            return false;
        }
    }

    internal static void Write(string path, MasterElectionService.MasterFileData data)
        => FileHelper.WriteAllText(path, JsonSerializer.Serialize(data, JsonOptions));

    internal static MasterElectionService.MasterFileData? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<MasterElectionService.MasterFileData>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
///     A claim held by this process. Only the holder can publish a port, beat or release it: every
///     write re-checks that the file still names our PID, so a claim that was reclaimed while we were
///     running is never silently overwritten.
/// </summary>
public sealed class MasterLockHandle(string path, int pid, DateTime startedAt, ILogger? logger = null)
{
    public string Path { get; } = path;
    public int Pid { get; } = pid;

    /// <summary>When this process took the claim. Kept for diagnostics and crash-log lines.</summary>
    public DateTime StartedAt { get; } = startedAt;

    /// <summary>The port recorded in the claim, or 0 while the server has not bound yet.</summary>
    public int Port { get; private set; }

    /// <summary>
    ///     Records the port the server actually bound, turning a bare claim into one a sibling launch
    ///     can attach to.
    /// </summary>
    public void Publish(int port, string scheme)
    {
        Port = port;
        WriteOwned(data =>
        {
            data.Port = port;
            data.Scheme = scheme;
            data.Heartbeat = DateTime.UtcNow;
        });
    }

    public void Heartbeat() => WriteOwned(data => data.Heartbeat = DateTime.UtcNow);

    /// <summary>Deletes the claim, but only while it still names this process.</summary>
    public void Release()
    {
        try
        {
            var existing = MasterLock.Read(Path);
            if (existing != null && existing.Pid != Pid)
                return;

            if (File.Exists(Path))
                File.Delete(Path);
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to release master lock at {Path}", Path);
        }
        finally
        {
            if (ReferenceEquals(MasterLock.Current, this))
                MasterLock.Current = null;
        }
    }

    /// <summary>
    ///     Whether the on-disk claim still names this process. A <c>false</c> here is the one realistic
    ///     way a running instance loses mastership: another launch judged our claim stale and took it.
    /// </summary>
    public bool StillOwned()
    {
        var existing = MasterLock.Read(Path);
        return existing != null && existing.Pid == Pid;
    }

    private void WriteOwned(Action<MasterElectionService.MasterFileData> mutate)
    {
        try
        {
            var data = MasterLock.Read(Path);
            if (data == null || data.Pid != Pid)
                return;

            mutate(data);
            MasterLock.Write(Path, data);
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to update master lock at {Path}", Path);
        }
    }
}
