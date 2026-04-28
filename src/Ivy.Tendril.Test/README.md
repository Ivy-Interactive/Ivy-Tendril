# Ivy.Tendril.Test

## Test Fixtures Guide

This project uses xUnit's fixture patterns to manage test setup and teardown. Choose the appropriate fixture based on your test's isolation requirements.

### Per-Test Fixtures

**`TempDirectoryFixture`** — Creates a temporary directory for each test

**When to use:**
- Tests need isolated file system state
- Tests modify files or directories
- Tests create config files or other artifacts

**Example:**
```csharp
public class MyTest : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("my-test");

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    [Fact]
    public void Should_Write_File()
    {
        var path = Path.Combine(_tempDir.Path, "test.txt");
        File.WriteAllText(path, "content");
        Assert.True(File.Exists(path));
    }
}
```

### Per-Class Fixtures (IClassFixture<T>)

**`ConfigServiceFixture`** — Shares a ConfigService instance across all tests in a class

**When to use:**
- Multiple tests need to read the same config structure
- Tests don't modify config state
- Expensive config initialization (file writes, YAML parsing)

**Example:**
```csharp
public class MyConfigTests : IClassFixture<ConfigServiceFixture>
{
    private readonly ConfigServiceFixture _fixture;

    public MyConfigTests(ConfigServiceFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Should_Read_Projects()
    {
        var projects = _fixture.Service.Settings.Projects;
        Assert.NotEmpty(projects);
    }
}
```

**`DatabaseFixture`** — Shares an in-memory SQLite database with migrations applied

**When to use:**
- Multiple tests need database schema but not shared data
- Tests modify database state but wrap operations in transactions
- Expensive migration setup (schema creation, indexes)

**Example:**
```csharp
public class MyDatabaseTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public MyDatabaseTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Should_Insert_Plan()
    {
        using var transaction = _fixture.Connection.BeginTransaction();
        try
        {
            using var cmd = _fixture.Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO Plans (...) VALUES (...)";
            cmd.ExecuteNonQuery();
            
            // Test assertions here
            
            transaction.Rollback(); // Keep database clean for next test
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}
```

### Choosing the Right Fixture

| Requirement | Use |
|-------------|-----|
| Need isolated file system per test | `TempDirectoryFixture` (per-test) |
| Multiple tests read same config | `ConfigServiceFixture` (per-class) |
| Need database schema, isolated data | `DatabaseFixture` (per-class) + transactions |
| Need to modify config between tests | `TempDirectoryFixture` (per-test) |

### Migration Guide

If your test class currently uses per-test setup that could be shared:

**Before (per-test setup):**
```csharp
public class ConfigServiceTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("ivy-config-test");

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    [Fact]
    public void Test1() { /* uses _tempDir */ }

    [Fact]
    public void Test2() { /* uses _tempDir */ }
}
```

**After (per-class fixture):**
```csharp
public class ConfigServiceTests : IClassFixture<ConfigServiceFixture>
{
    private readonly ConfigServiceFixture _fixture;

    public ConfigServiceTests(ConfigServiceFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Test1() { /* uses _fixture.Service */ }

    [Fact]
    public void Test2() { /* uses _fixture.Service */ }
}
```

**Note:** Only migrate to per-class fixtures if tests don't need isolation. If tests modify state, keep per-test fixtures.
