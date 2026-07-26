using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System;

namespace Ivy.Tendril.Database.Migrations;

public class Migration_022_AddIsSystemToWorkflows : IMigration
{
    public int Version => 22;
    public string Description => "Add IsSystem column to Workflows table and seed default system lifecycle workflows";

    public void Apply(SqliteConnection connection, ILogger? logger = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            -- 1. Add column
            ALTER TABLE Workflows ADD COLUMN IsSystem INTEGER NOT NULL DEFAULT 0;

            -- 2. Seed default Tendril Core Lifecycle workflow
            INSERT INTO Workflows (Name, Description, Project, Definition, IsActive, IsSystem, Created, Updated)
            VALUES (
                'Tendril Core Lifecycle',
                'Core software engineering lifecycle workflow: from draft creation, to logic reviews, implementation, test verification, and automated PR generation.',
                'default',
                '{"steps":[{"id":"start","name":"Start","type":"Trigger","connectionName":"","action":"","args":"{}","next":["create_draft"],"x":50,"y":200},{"id":"create_draft","name":"Create_Draft_Plan","type":"Prompt","connectionName":"","action":"","args":"Initialize the plan draft file, check requirements, analyze code context, and draft a structured implementation plan.","provider":"CodeQuality","model":"default","next":["review_draft"],"x":380,"y":200},{"id":"review_draft","name":"Review_Draft_Plan","type":"Prompt","connectionName":"","action":"","args":"Evaluate the generated plan draft. Verify it aligns with architecture style rules, has clean spacing, and defines comprehensive test verifications.","provider":"CodeQuality","model":"default","next":["implement_draft"],"x":710,"y":200},{"id":"implement_draft","name":"Implement_Plan","type":"Prompt","connectionName":"","action":"","args":"Implement the approved changes to codebase source files according to the implementation plan details.","provider":"CodeQuality","model":"default","next":["review_implementation"],"x":1040,"y":200},{"id":"review_implementation","name":"Review_Implementation","type":"Prompt","connectionName":"","action":"","args":"Verify implementation correctness. Run syntax linting, execute automated tests, and perform a security scan on diff additions.","provider":"CodeSecurity","model":"default","next":["create_pr"],"x":1370,"y":200},{"id":"create_pr","name":"Create_Pull_Request","type":"Prompt","connectionName":"","action":"","args":"Automatically check out a Git branch, commit modifications, push code to remote, and build a Pull Request targeting the main branch.","provider":"CodeQuality","model":"default","next":[],"x":1700,"y":200}]}',
                1,
                1,
                '2026-07-16T12:00:00.0000000Z',
                '2026-07-16T12:00:00.0000000Z'
            );
            """;
        cmd.ExecuteNonQuery();

        using var setVersionCmd = connection.CreateCommand();
        setVersionCmd.CommandText = "PRAGMA user_version = 22;";
        setVersionCmd.ExecuteNonQuery();
    }
}
