namespace Ivy.Tendril.Test;

public class TempDirectoryFixture : IDisposable
{
    public string Path { get; }

    public TempDirectoryFixture(string prefix = "tendril-test")
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            try
            {
                ClearReadOnlyAttributes(Path);
                Directory.Delete(Path, true);
            }
            catch
            {
                /* best effort cleanup */
            }
    }

    private static void ClearReadOnlyAttributes(string path)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var attrs = File.GetAttributes(file);
                    if ((attrs & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
                }
                catch { }
            }
        }
        catch { }
    }
}
