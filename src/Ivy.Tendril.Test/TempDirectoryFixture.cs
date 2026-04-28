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
                Directory.Delete(Path, true);
            }
            catch
            {
                /* best effort cleanup */
            }
    }
}
