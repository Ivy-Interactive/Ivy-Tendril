namespace Ivy.Tendril.Helpers;

public static class JobIdAllocator
{
    private static readonly object CounterLock = new();

    public static string AllocateJobId(string tendrilHome)
    {
        var jobsDir = Path.Combine(tendrilHome, "Jobs");
        Directory.CreateDirectory(jobsDir);
        var counterFile = Path.Combine(jobsDir, ".counter");

        lock (CounterLock)
        {
            var timeout = TimeSpan.FromSeconds(10);
            var startTime = DateTime.UtcNow;
            var retryDelay = 50;

            while (true)
            {
                try
                {
                    using var stream = new FileStream(
                        counterFile,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        bufferSize: 4096,
                        FileOptions.None);

                    var counter = 1;
                    if (stream.Length > 0)
                    {
                        using var reader = new StreamReader(stream, leaveOpen: true);
                        var text = reader.ReadToEnd().Trim();
                        if (int.TryParse(text, out var parsed))
                            counter = parsed;
                    }

                    var id = counter.ToString("D5");

                    stream.SetLength(0);
                    stream.Position = 0;
                    using (var writer = new StreamWriter(stream, leaveOpen: true))
                    {
                        writer.Write((counter + 1).ToString());
                        writer.Flush();
                    }

                    return id;
                }
                catch (IOException) when (DateTime.UtcNow - startTime < timeout)
                {
                    Thread.Sleep(retryDelay);
                    retryDelay = Math.Min(retryDelay * 2, 500);
                }
                catch (IOException)
                {
                    throw new TimeoutException(
                        $"Failed to acquire lock on {counterFile} after {timeout.TotalSeconds} seconds.");
                }
            }
        }
    }

    /// <summary>
    /// Seeds the counter from existing job logs so new IDs don't collide with old ones.
    /// Call once at startup if the counter file doesn't exist yet.
    /// </summary>
    public static void SeedIfNeeded(string tendrilHome)
    {
        var jobsDir = Path.Combine(tendrilHome, "Jobs");
        Directory.CreateDirectory(jobsDir);
        var counterFile = Path.Combine(jobsDir, ".counter");

        if (File.Exists(counterFile)) return;

        var max = ScanMaxLogNumber(jobsDir);
        if (max <= 0) return;

        lock (CounterLock)
        {
            if (File.Exists(counterFile)) return;

            try
            {
                using var stream = new FileStream(
                    counterFile,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);
                using var writer = new StreamWriter(stream);
                writer.Write((max + 1).ToString());
            }
            catch (IOException)
            {
                // Another process created it first — that's fine
            }
        }
    }

    /// <summary>
    /// Highest job id among the job logs in <paramref name="jobsDir"/>. Job log names are stems of the
    /// form <c>{jobId}-{planId}-{type}</c> or <c>{jobId}-{type}</c>, so only the leading segment counts.
    /// </summary>
    internal static int ScanMaxLogNumber(string jobsDir)
    {
        var max = 0;
        try
        {
            if (Directory.Exists(jobsDir))
            {
                foreach (var file in Directory.GetFiles(jobsDir, "*.md"))
                {
                    var name = Path.GetFileName(file);
                    var dash = name.IndexOf('-');
                    if (dash <= 0) continue;
                    if (int.TryParse(name[..dash], out var num) && num > max)
                        max = num;
                }
            }
        }
        catch
        {
            // Best-effort scan
        }

        return max;
    }
}
