using System.Collections.Concurrent;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Helpers;

/// <summary>
/// Reads a job's eventwire file back into memory.
/// </summary>
/// <remarks>
/// There is no writer and no deleter here. <see cref="JobItem"/> appends each event to the file as it is
/// produced, so the record on disk stays complete even when the process is killed mid-run — persisting it
/// at completion would lose exactly the runs worth investigating. Job logs are never deleted; deleting a
/// job removes it from the UI and the database, not its forensic record.
/// Rehydration takes only the tail, since the in-memory queue is capped at
/// <see cref="JobItem.MaxOutputLines"/> anyway.
/// </remarks>
public static class JobEventWireStore
{
    public static ConcurrentQueue<string>? Read(string tendrilHome, JobItem job)
    {
        try
        {
            var path = JobLogPaths.EventWire(tendrilHome, job);
            if (!File.Exists(path))
            {
                // Jobs that finished before the artifacts moved to a stem-based name wrote a flat
                // <jobId>.eventwire.jsonl. Reading it keeps their output visible in the Jobs UI after an
                // upgrade; nothing writes this name any more.
                var legacy = Path.Combine(JobLogPaths.JobsDir(tendrilHome), $"{job.Id}.eventwire.jsonl");
                if (!File.Exists(legacy)) return null;
                path = legacy;
            }

            var lines = ReadAllLinesShared(path);
            if (lines.Count > JobItem.MaxOutputLines)
                lines = lines[^JobItem.MaxOutputLines..];
            return new ConcurrentQueue<string>(lines);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a file that may still be open for writing. <see cref="File.ReadAllLines(string)"/> requests
    /// <see cref="FileShare.Read"/>, which denies the live appender its write handle and fails with a
    /// sharing violation — so a running job's log could not be read at all.
    /// </summary>
    public static List<string> ReadAllLinesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
            lines.Add(line);
        return lines;
    }
}
