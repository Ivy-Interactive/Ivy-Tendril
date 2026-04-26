namespace Ivy.Tendril.Test;

public class TempDirectoryFixtureTests
{
    [Fact]
    public void Should_Create_Directory_On_Construction()
    {
        using var fixture = new TempDirectoryFixture();

        Assert.NotNull(fixture.Path);
        Assert.True(Directory.Exists(fixture.Path));
    }

    [Fact]
    public void Should_Delete_Directory_On_Dispose()
    {
        string path;

        using (var fixture = new TempDirectoryFixture())
        {
            path = fixture.Path;
            Assert.True(Directory.Exists(path));
        }

        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public void Should_Use_Custom_Prefix()
    {
        using var fixture = new TempDirectoryFixture("custom-prefix");

        var directoryName = System.IO.Path.GetFileName(fixture.Path);
        Assert.StartsWith("custom-prefix-", directoryName);
    }

    [Fact]
    public void Should_Create_Unique_Paths_For_Multiple_Instances()
    {
        using var fixture1 = new TempDirectoryFixture();
        using var fixture2 = new TempDirectoryFixture();

        Assert.NotEqual(fixture1.Path, fixture2.Path);
        Assert.True(Directory.Exists(fixture1.Path));
        Assert.True(Directory.Exists(fixture2.Path));
    }
}
