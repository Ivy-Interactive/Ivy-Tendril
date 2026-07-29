using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System;

namespace Ivy.Tendril.Database.Migrations;

public class Migration_020_SeedDefaultWorkflows : IMigration
{
    public int Version => 20;
    public string Description => "Seed default Code Quality and Code Security workflows";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        var nowStr = DateTime.UtcNow.ToString("o");

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO Workflows (Name, Description, Definition, IsActive, Created, Updated)
            VALUES (
                'Code Quality Audit',
                'Analyze the codebase changes and run quality checks to identify formatting, complexity, or redundancy issues.',
                '{"steps":[{"id":"start","name":"Start","type":"Trigger","connectionName":"","action":"","args":"{}","next":["checkquality"]},{"id":"checkquality","name":"CheckQuality","type":"Prompt","connectionName":"","action":"","args":"Analyze the codebase for formatting violations, complex logic, anti-patterns, resource leaks, or missing unit tests.","provider":"CodeQuality","model":"default","next":[]}]}',
                1,
                @Now,
                @Now
            );

            INSERT OR IGNORE INTO Workflows (Name, Description, Definition, IsActive, Created, Updated)
            VALUES (
                'Code Security Scan',
                'Run a security scan to find credentials leak, insecure dependencies, or vulnerabilities.',
                '{"steps":[{"id":"start","name":"Start","type":"Trigger","connectionName":"","action":"","args":"{}","next":["scansecurity"]},{"id":"scansecurity","name":"ScanSecurity","type":"Prompt","connectionName":"","action":"","args":"Inspect the codebase for hardcoded secrets, insecure package dependencies, SQL injection/XSS risks, and weak crypto configurations.","provider":"CodeSecurity","model":"default","next":[]}]}',
                1,
                @Now,
                @Now
            );
            """;
        cmd.Parameters.AddWithValue("@Now", nowStr);
        cmd.ExecuteNonQuery();

        using var setVersionCmd = connection.CreateCommand();
        setVersionCmd.CommandText = "PRAGMA user_version = 20;";
        setVersionCmd.ExecuteNonQuery();
    }
}
