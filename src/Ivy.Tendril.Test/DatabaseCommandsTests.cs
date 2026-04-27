using Ivy.Tendril.Database;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Test;

public class DatabaseCommandsTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly string _dbPath;
    private readonly TestLogger _logger = new();

    public DatabaseCommandsTests()
    {
        _dbPath = Path.Combine(_tempDir.Path, "tendril.db");
    }

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    [Fact]
    public void DbVersion_ShowsCurrentAndLatestVersion()
    {
        // Initialize DB with migrations first
        DatabaseCommands.DbMigrateInternal(_dbPath);

        _logger.Clear();
        var result = DatabaseCommands.DbVersionInternal(_dbPath, _logger);
        Assert.Equal(0, result);

        var output = _logger.GetOutput();
        Assert.Contains("Database version:", output);
        Assert.Contains("Latest version:", output);
        Assert.Contains("Status:", output);
        Assert.Contains("Up to date", output);
    }

    [Fact]
    public void DbMigrate_AppliesPendingMigrations()
    {
        var result = DatabaseCommands.DbMigrateInternal(_dbPath, _logger);
        Assert.Equal(0, result);

        // Verify migrations were applied by checking version
        _logger.Clear();
        DatabaseCommands.DbVersionInternal(_dbPath, _logger);
        var versionOutput = _logger.GetOutput();
        Assert.Contains("Up to date", versionOutput);
    }

    [Fact]
    public void DbMigrate_WhenUpToDate_ReportsUpToDate()
    {
        // Run migrations first
        DatabaseCommands.DbMigrateInternal(_dbPath);

        // Run again — should report up to date
        _logger.Clear();
        var result = DatabaseCommands.DbMigrateInternal(_dbPath, _logger);
        Assert.Equal(0, result);

        var output = _logger.GetOutput();
        Assert.Contains("up to date", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DbReset_DropsTablesAndReappliesMigrations()
    {
        // Apply migrations to create tables
        DatabaseCommands.DbMigrateInternal(_dbPath);

        // Reset with --force to skip confirmation
        _logger.Clear();
        var result = DatabaseCommands.DbResetInternal(_dbPath, true, _logger);
        Assert.Equal(0, result);

        var output = _logger.GetOutput();
        Assert.Contains("Resetting database", output);
        Assert.Contains("Dropping existing tables", output);

        // Verify migrations were re-applied
        _logger.Clear();
        DatabaseCommands.DbVersionInternal(_dbPath, _logger);
        var versionOutput = _logger.GetOutput();
        Assert.Contains("Up to date", versionOutput);
    }

    [Fact]
    public void DbReset_WithoutForce_AbortsOnNonYResponse()
    {
        DatabaseCommands.DbMigrateInternal(_dbPath);

        var output = CaptureConsoleOutputWithInput("n\n", () =>
        {
            var result = DatabaseCommands.DbResetInternal(_dbPath, false, _logger);
            Assert.Equal(1, result);
        });

        // "Aborted" is still written to Console (interactive UI)
        Assert.Contains("Aborted", output);
    }

    [Fact]
    public void DbVersion_ReturnsZero()
    {
        DatabaseCommands.DbMigrateInternal(_dbPath);
        Assert.Equal(0, DatabaseCommands.DbVersionInternal(_dbPath, _logger));
    }

    [Fact]
    public void DbMigrate_ReturnsZero()
    {
        Assert.Equal(0, DatabaseCommands.DbMigrateInternal(_dbPath, _logger));
    }

    [Fact]
    public void DbReset_WithForce_ReturnsZero()
    {
        DatabaseCommands.DbMigrateInternal(_dbPath);
        Assert.Equal(0, DatabaseCommands.DbResetInternal(_dbPath, true, _logger));
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

    /// <summary>
    /// Simple test logger that captures log messages for assertions.
    /// </summary>
    private class TestLogger : ILogger
    {
        private readonly List<string> _messages = [];

        public void Clear() => _messages.Clear();

        public string GetOutput() => string.Join(Environment.NewLine, _messages);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _messages.Add(formatter(state, exception));
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }
}