namespace Ivy.Tendril.Test.TestHelpers;

/// <summary>Locates the repo root from the test binary, for tests that audit source text.</summary>
internal static class RepoRoot
{
    public static string Find()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "Ivy.Tendril", "Ivy.Tendril.slnx")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate repository root from BaseDirectory: " + AppDomain.CurrentDomain.BaseDirectory);
    }
}
