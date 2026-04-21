using Ivy.Tendril.Database;

namespace Ivy.Tendril.Test;

public class DatabaseCommandsTests : IDisposable
{
    private readonly string _dbPath;

    public DatabaseCommandsTests()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tendril-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        _dbPath = Path.Combine(tempDir, "tendril.db");
    }

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_dbPath)!;
        if (Directory.Exists(dir))
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                /* best effort cleanup */
            }
    }

    [Fact]
    public void DbVersion_ShowsCurrentAndLatestVersion()
    {
        // Initialize DB with migrations first
        DatabaseCommands.DbMigrateInternal(_dbPath);

        var output = CaptureConsoleOutput(() =>
        {
            var result = DatabaseCommands.DbVersionInternal(_dbPath);
            Assert.Equal(0, result);
        });

        Assert.Contains("Database version:", output);
        Assert.Contains("Latest version:", output);
        Assert.Contains("Status:", output);
        Assert.Contains("Up to date", output);
    }

    [Fact]
    public void DbMigrate_AppliesPendingMigrations()
    {
        var output = CaptureConsoleOutput(() =>
        {
            var result = DatabaseCommands.DbMigrateInternal(_dbPath);
            Assert.Equal(0, result);
        });

        // Verify migrations were applied by checking version
        var versionOutput = CaptureConsoleOutput(() => DatabaseCommands.DbVersionInternal(_dbPath));
        Assert.Contains("Up to date", versionOutput);
    }

    [Fact]
    public void DbMigrate_WhenUpToDate_ReportsUpToDate()
    {
        // Run migrations first
        DatabaseCommands.DbMigrateInternal(_dbPath);

        // Run again — should report up to date
        var output = CaptureConsoleOutput(() =>
        {
            var result = DatabaseCommands.DbMigrateInternal(_dbPath);
            Assert.Equal(0, result);
        });

        Assert.Contains("up to date", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DbReset_DropsTablesAndReappliesMigrations()
    {
        // Apply migrations to create tables
        DatabaseCommands.DbMigrateInternal(_dbPath);

        // Reset with --force to skip confirmation
        var output = CaptureConsoleOutput(() =>
        {
            var result = DatabaseCommands.DbResetInternal(_dbPath, true);
            Assert.Equal(0, result);
        });

        Assert.Contains("Resetting database", output);
        Assert.Contains("Dropping existing tables", output);

        // Verify migrations were re-applied
        var versionOutput = CaptureConsoleOutput(() => DatabaseCommands.DbVersionInternal(_dbPath));
        Assert.Contains("Up to date", versionOutput);
    }

    [Fact]
    public void DbReset_WithoutForce_AbortsOnNonYResponse()
    {
        DatabaseCommands.DbMigrateInternal(_dbPath);

        var output = CaptureConsoleOutputWithInput("n\n", () =>
        {
            var result = DatabaseCommands.DbResetInternal(_dbPath, false);
            Assert.Equal(1, result);
        });

        Assert.Contains("Aborted", output);
    }

    [Fact]
    public void Handle_UnknownCommand_ReturnsNegativeOne()
    {
        var result = DatabaseCommands.Handle(["unknown-command"]);
        Assert.Equal(-1, result);
    }

    [Fact]
    public void Handle_EmptyArgs_ReturnsNegativeOne()
    {
        var result = DatabaseCommands.Handle([]);
        Assert.Equal(-1, result);
    }

    [Fact]
    public void Handle_DbVersion_ReturnsZero()
    {
        DatabaseCommands.DbMigrateInternal(_dbPath);
        CaptureConsoleOutput(() => { Assert.Equal(0, DatabaseCommands.DbVersionInternal(_dbPath)); });
    }

    [Fact]
    public void Handle_DbMigrate_ReturnsZero()
    {
        CaptureConsoleOutput(() => { Assert.Equal(0, DatabaseCommands.DbMigrateInternal(_dbPath)); });
    }

    [Fact]
    public void Handle_DbReset_WithForce_ReturnsZero()
    {
        DatabaseCommands.DbMigrateInternal(_dbPath);
        CaptureConsoleOutput(() => { Assert.Equal(0, DatabaseCommands.DbResetInternal(_dbPath, true)); });
    }

    private static string CaptureConsoleOutput(Action action)
    {
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            action();
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    private static string CaptureConsoleOutputWithInput(string input, Action action)
    {
        var originalOut = Console.Out;
        var originalIn = Console.In;
        using var writer = new StringWriter();
        using var reader = new StringReader(input);
        Console.SetOut(writer);
        Console.SetIn(reader);
        try
        {
            action();
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetIn(originalIn);
        }
    }
}