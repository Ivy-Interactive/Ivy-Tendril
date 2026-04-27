namespace Ivy.Tendril.Test;

public class DatabaseFixtureTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public DatabaseFixtureTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Fixture_Creates_Open_Connection()
    {
        Assert.NotNull(_fixture.Connection);
        Assert.Equal(System.Data.ConnectionState.Open, _fixture.Connection.State);
    }

    [Fact]
    public void Fixture_Applies_Migrations()
    {
        using var cmd = _fixture.Connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.True(version > 0, "Database should have migrations applied");
    }

    [Fact]
    public void Fixture_Creates_Plans_Table()
    {
        using var cmd = _fixture.Connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='Plans';";
        var result = cmd.ExecuteScalar();
        Assert.NotNull(result);
        Assert.Equal("Plans", result);
    }

    [Fact]
    public void Fixture_Enables_Foreign_Keys()
    {
        using var cmd = _fixture.Connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys;";
        var result = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(1, result);
    }
}
