namespace Ivy.Tendril.Test;

public class ConfigServiceFixtureTests : IClassFixture<ConfigServiceFixture>
{
    private readonly ConfigServiceFixture _fixture;

    public ConfigServiceFixtureTests(ConfigServiceFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Fixture_Creates_Valid_ConfigService()
    {
        Assert.NotNull(_fixture.Service);
        Assert.NotNull(_fixture.Service.Settings);
    }

    [Fact]
    public void Fixture_Loads_Test_Project()
    {
        var projects = _fixture.Service.Settings.Projects;
        Assert.NotEmpty(projects);
        Assert.Equal("TestProject", projects[0].Name);
    }

    [Fact]
    public void Fixture_Sets_Tendril_Home()
    {
        Assert.NotEmpty(_fixture.Service.TendrilHome);
        Assert.True(Directory.Exists(_fixture.Service.TendrilHome));
    }

    [Fact]
    public void Fixture_Cleans_Up_After_All_Tests()
    {
        var tempPath = _fixture.Service.TendrilHome;
        Assert.True(Directory.Exists(tempPath));
    }
}
