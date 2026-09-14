using System.Diagnostics;
using System.Text.Json;
using Ivy.Helpers;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Helpers;

/// <summary>
///     What a reclaim is allowed to do about a claim whose PID is still running.
/// </summary>
public enum ReclaimPolicy
{
    /// <summary>Never delete a claim whose PID is running: a saturated master is not a dead one.</summary>
    RespectLiveProcess,

    /// <summary>Evict anyway. Only for a caller that has already proved the holder is not serving.</summary>
    ForceEvictLiveProcess
}

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
    /// <summary>How often the holder of a claim writes a heartbeat into it.</summary>
    public static readonly TimeSpan HeartbeatPeriod = TimeSpan.FromSeconds(30);

    /// <summary>How many beats a claim may miss before anyone is allowed to call it abandoned.</summary>
    public const int MissedBeatsBeforeStale = 3;

    /// <summary>
    ///     A claim whose heartbeat is older than this is treated as abandoned. 90s: three missed beats
    ///     of <see cref="HeartbeatPeriod" />, not a magic number, so the two cannot drift apart.
    /// </summary>
    public static TimeSpan StaleAfter => HeartbeatPeriod * MissedBeatsBeforeStale;

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
        Log(logger, $"claimed the master lock (PID {data.Pid})");
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
            data = JsonSerializer.Deserialize<MasterElectionService.MasterFileData>(ReadAllShared(path), JsonOptions);
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
    /// <param name="policy">
    ///     Under the default <see cref="ReclaimPolicy.RespectLiveProcess" /> a claim rejected only for
    ///     the age of its heartbeat is left alone while its PID is running: at load average 33-56 a
    ///     starved 30s timer overshoots the 90s window, and deleting the claim of a master that is very
    ///     much alive is what broke every CLI command in the 2026-09-14 14:57 incident. A caller that
    ///     has already probed the holder and found it silent passes
    ///     <see cref="ReclaimPolicy.ForceEvictLiveProcess" />.
    /// </param>
    public static bool TryReclaimStale(string tendrilHome, ILogger? logger = null,
        ReclaimPolicy policy = ReclaimPolicy.RespectLiveProcess)
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

        if (policy == ReclaimPolicy.RespectLiveProcess && IsStaleHeartbeatOfALiveProcess(path, reason, out var live))
        {
            var age = DateTime.UtcNow - live!.Heartbeat;
            Log(logger,
                $"refusing to reclaim .master: PID {live.Pid} is alive (heartbeat {age.TotalSeconds:0}s old, " +
                $"threshold {StaleAfter.TotalSeconds:0}s); use 'tendril master release' to break it deliberately");
            return false;
        }

        try
        {
            File.Delete(path);
            Log(logger, $"reclaimed stale .master file ({reason})");
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to delete stale .master file at {Path}", path);
            return false;
        }
    }

    /// <summary>
    ///     Deletes the claim outright, whatever it holds and whoever holds it. The operator escape hatch
    ///     behind <c>tendril master release</c>: every automatic path goes through
    ///     <see cref="TryReclaimStale" /> instead, so nothing but a deliberate human decision reaches
    ///     this.
    /// </summary>
    public static bool ForceRelease(string tendrilHome, ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(tendrilHome))
            return false;

        var path = GetMasterFilePath(tendrilHome);
        if (!File.Exists(path))
            return true;

        var existing = Read(path);

        try
        {
            File.Delete(path);
            Log(logger, existing == null
                ? "master release: deleted an unreadable .master file"
                : $"master release: deleted the claim held by PID {existing.Pid}");

            if (existing != null && Current != null && existing.Pid == Current.Pid)
                Current = null;

            return true;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to delete .master file at {Path}", path);
            return false;
        }
    }

    /// <summary>
    ///     Whether <see cref="ReadLiveMaster" /> rejected the claim for the age of its heartbeat alone,
    ///     while the PID it names is still running. That combination is ambiguous from the file: a
    ///     saturated master looks exactly like a hung one.
    /// </summary>
    private static bool IsStaleHeartbeatOfALiveProcess(string path, string? rejectReason,
        out MasterElectionService.MasterFileData? data)
    {
        data = null;

        if (rejectReason?.StartsWith("heartbeat", StringComparison.Ordinal) != true)
            return false;

        data = Read(path);
        return data != null && IsProcessAlive(data.Pid);
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

    /// <summary>
    ///     Replaces the claim atomically. A truncate-in-place write publishes a zero-byte claim to every
    ///     concurrent reader for the width of the write, and every heartbeat opens that window once:
    ///     observed 2026-09-14 13:52Z, where an empty <c>.master</c> made the CLI report "unreadable"
    ///     and delete the claim of a live, listening master. Writing beside the file and moving over it
    ///     means a reader sees either the old claim or the new one, never neither.
    /// </summary>
    internal static void Write(string path, MasterElectionService.MasterFileData data)
    {
        // Same directory as the target: a cross-device move is a copy, and a copy is not atomic.
        var tmp = path + ".tmp";

        try
        {
            FileHelper.WriteAllText(tmp, JsonSerializer.Serialize(data, JsonOptions));
            MoveOverwriting(tmp, path);
        }
        catch
        {
            // A crashed write must not leave a stray .tmp behind for the next one to trip over.
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch
            {
                // Best effort cleanup
            }

            throw;
        }
    }

    /// <summary>
    ///     <see cref="File.Move(string, string, bool)" /> is atomic on APFS, ext4 and NTFS, but on
    ///     Windows it still fails while a reader holds the destination without share-delete. Readers go
    ///     through <see cref="ReadAllShared" /> for exactly that reason; the retries cover anything else
    ///     holding the file (a virus scanner, an editor) so a beat is not lost to a transient lock.
    /// </summary>
    private static void MoveOverwriting(string source, string destination)
    {
        for (var attempt = 0; ; attempt++)
            try
            {
                File.Move(source, destination, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(20);
            }
    }

    internal static MasterElectionService.MasterFileData? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<MasterElectionService.MasterFileData>(ReadAllShared(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Reads the claim while allowing concurrent writers and deleters, so a reader neither blocks the
    ///     atomic replace in <see cref="Write" /> nor is blocked by it.
    /// </summary>
    private static string ReadAllShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    ///     Writes a claim only when there is no file to overwrite, the same <see cref="FileMode.CreateNew" />
    ///     race <see cref="TryAcquire" /> uses. Returns <c>false</c> when someone else got there first.
    /// </summary>
    internal static bool TryWriteNew(string path, MasterElectionService.MasterFileData data)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);
            writer.Write(JsonSerializer.Serialize(data, JsonOptions));
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Claim transitions go to <c>crash.log</c> as well as the logger. It is the one sink that
    ///     survives a wedged server and the one an operator actually reads: the 14:57 incident wrote
    ///     zero lines about the claim it lost, which is why it took 37 minutes to see.
    /// </summary>
    internal static void Log(ILogger? logger, string message)
    {
        logger?.LogInformation("MasterLock: {Message}", message);
        CrashLog.Write($"[{DateTime.UtcNow:O}] MasterLock (PID {Environment.ProcessId}): {message}");
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
    ///     The scheme recorded in the claim. Remembered rather than re-derived, because re-asserting a
    ///     claim that went missing has to write back the same URL a sibling would have attached to.
    /// </summary>
    public string Scheme { get; private set; } = "http";

    /// <summary>What a beat found on disk, and therefore what it did about it.</summary>
    public enum HeartbeatOutcome
    {
        /// <summary>The claim still named us: heartbeat written, nothing else to do.</summary>
        Beat,

        /// <summary>The claim was missing, empty or held by a dead PID, and is ours again.</summary>
        Reasserted,

        /// <summary>A different, live process holds the claim. The one case that is a real loss.</summary>
        Dispossessed
    }

    /// <summary>
    ///     Records the port the server actually bound, turning a bare claim into one a sibling launch
    ///     can attach to.
    /// </summary>
    public void Publish(int port, string scheme)
    {
        Port = port;
        Scheme = string.IsNullOrEmpty(scheme) ? "http" : scheme;
        WriteOwned(data =>
        {
            data.Port = port;
            data.Scheme = Scheme;
            data.Heartbeat = DateTime.UtcNow;
        });
        MasterLock.Log(logger, $"published port {port} ({Scheme}) for PID {Pid}");
    }

    public void Heartbeat() => WriteOwned(data => data.Heartbeat = DateTime.UtcNow);

    /// <inheritdoc cref="BeatOrReassert(out int)" />
    public HeartbeatOutcome BeatOrReassert() => BeatOrReassert(out _);

    /// <summary>
    ///     Beats the claim, re-taking it when it went missing, and reporting the one case that is a real
    ///     loss of mastership: a claim now held by a different, live process.
    /// </summary>
    /// <param name="foreignPid">
    ///     The live PID that took the claim, when the outcome is
    ///     <see cref="HeartbeatOutcome.Dispossessed" />; 0 otherwise.
    /// </param>
    /// <remarks>
    ///     A missing, empty or unreadable claim is not evidence that anyone took mastership. It is what a
    ///     truncating write looks like mid-flight and what a reclaim of a live master leaves behind, so
    ///     the only honest response is to write the claim back rather than to stop being the master over
    ///     a file that went missing while 28 agents were running.
    /// </remarks>
    public HeartbeatOutcome BeatOrReassert(out int foreignPid)
    {
        foreignPid = 0;
        var existing = MasterLock.Read(Path);

        if (existing != null && existing.Pid == Pid)
        {
            Heartbeat();
            return HeartbeatOutcome.Beat;
        }

        if (existing != null && MasterLock.IsProcessAlive(existing.Pid))
        {
            foreignPid = existing.Pid;
            return HeartbeatOutcome.Dispossessed;
        }

        return Reassert(out foreignPid);
    }

    /// <summary>Deletes the claim, but only while it demonstrably still names this process.</summary>
    /// <remarks>
    ///     Positive ownership, not "it is not someone else's": <see cref="MasterLock.Read" /> returns
    ///     <c>null</c> for an unreadable file too, and deleting a claim we cannot prove is ours is how a
    ///     shutdown ends up leaving no master behind at all. A corrupt claim left in place is still
    ///     recoverable, because the next reader rejects it as unreadable and reclaims it with no live PID
    ///     to respect.
    /// </remarks>
    public void Release()
    {
        try
        {
            var existing = MasterLock.Read(Path);
            if (existing == null || existing.Pid != Pid)
                return;

            if (File.Exists(Path))
                File.Delete(Path);

            MasterLock.Log(logger, $"released the claim (PID {Pid})");
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

    /// <summary>
    ///     Writes our claim back, keeping the original <see cref="StartedAt" /> so uptime is not reset by
    ///     a re-assert, and restores <see cref="MasterLock.Current" /> so the rest of the process sees a
    ///     held claim again.
    /// </summary>
    private HeartbeatOutcome Reassert(out int foreignPid)
    {
        foreignPid = 0;
        var data = new MasterElectionService.MasterFileData
        {
            Pid = Pid,
            Port = Port,
            Scheme = Scheme,
            StartedAt = StartedAt,
            Heartbeat = DateTime.UtcNow
        };

        if (!File.Exists(Path))
        {
            // CreateNew rather than a blind write: a sibling launch may be claiming in this very
            // millisecond, and losing that race means someone else is legitimately the master now.
            if (!MasterLock.TryWriteNew(Path, data))
            {
                var winner = MasterLock.Read(Path);
                if (winner != null && winner.Pid != Pid && MasterLock.IsProcessAlive(winner.Pid))
                {
                    foreignPid = winner.Pid;
                    return HeartbeatOutcome.Dispossessed;
                }

                MasterLock.Write(Path, data);
            }
        }
        else
        {
            // Unreadable, empty, or naming a dead PID. None of those is a claim anyone is holding.
            MasterLock.Write(Path, data);
        }

        MasterLock.Current = this;
        return HeartbeatOutcome.Reasserted;
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
