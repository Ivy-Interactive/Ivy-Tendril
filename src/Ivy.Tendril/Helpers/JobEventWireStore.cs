using System.Collections.Concurrent;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Helpers;

public static class JobEventWireStore
{
    public static string GetFilePath(string tendrilHome, string jobId)
    {
        var jobsDir = Path.Combine(tendrilHome, "Jobs");
        Directory.CreateDirectory(jobsDir);
        return Path.Combine(jobsDir, jobId + ".eventwire.jsonl");
    }

    public static void Write(string tendrilHome, JobItem job)
    {
        if (job.OutputLines.IsEmpty) return;
        try
        {
            File.WriteAllLines(GetFilePath(tendrilHome, job.Id), job.OutputLines);
        }
        catch
        {
            /* Best-effort persistence */
        }
    }

    public static ConcurrentQueue<string>? Read(string tendrilHome, string jobId)
    {
        var path = GetFilePath(tendrilHome, jobId);
        if (!File.Exists(path)) return null;
        try
        {
            return new ConcurrentQueue<string>(File.ReadAllLines(path));
        }
        catch
        {
            return null;
        }
    }

    public static void Delete(string tendrilHome, string jobId)
    {
        try
        {
            File.Delete(GetFilePath(tendrilHome, jobId));
        }
        catch
        {
            /* Best-effort cleanup */
        }
    }
}
