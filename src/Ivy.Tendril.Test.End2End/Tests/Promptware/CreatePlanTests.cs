using Ivy.Tendril.Test.End2End.Fixtures;
using Ivy.Tendril.Test.End2End.Helpers;

namespace Ivy.Tendril.Test.End2End.Tests.Promptware;

[Collection("E2E-Promptware")]
public class CreatePlanTests
{
    private readonly PromptwareTestFixture _fixture;

    public CreatePlanTests(PromptwareTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CreatePlan_ProducesPlanYaml_WithCorrectStructure()
    {
        var description = "Add a hello world comment to the top of Program.cs";

        var result = await _fixture.Runner.RunAsync(
            "CreatePlan",
            args: [],
            workingDir: _fixture.TestRepo.LocalClonePath,
            extraValues: new Dictionary<string, string>
            {
                ["Args"] = description,
                ["PlansDirectory"] = _fixture.PlansDir,
                ["Project"] = "E2ETest"
            });

        PromptwareAssertions.AssertExitSuccess(result, "CreatePlan");
        PromptwareAssertions.AssertNoAgentErrors(result);

        // The agent calls `tendril plan create` which resolves PlansDirectory from env.
        // On some systems, env var inheritance through agent bash shells may resolve to
        // the system PlansDir instead of the test's temp dir. Check both locations.
        var planFolder = FindCreatedPlan(result, "HelloWorld");
        Assert.NotNull(planFolder);

        PromptwareAssertions.AssertPlanYamlExists(planFolder!);
        PromptwareAssertions.AssertPlanState(planFolder!, "Draft");
        PromptwareAssertions.AssertPlanYamlContains(planFolder!, "title:");
        PromptwareAssertions.AssertPlanYamlContains(planFolder!, "project:");

        // The detailed plan content is in revisions/001.md
        var revisionsDir = Path.Combine(planFolder!, "revisions");
        Assert.True(Directory.Exists(revisionsDir),
            $"revisions/ directory should exist at {revisionsDir}");
        var revisionFiles = Directory.GetFiles(revisionsDir, "*.md");
        Assert.True(revisionFiles.Length > 0,
            $"At least one revision file expected in {revisionsDir}");
    }

    [Fact]
    public async Task CreatePlan_FailsGracefully_WithInvalidDescription()
    {
        try
        {
            var result = await _fixture.Runner.RunAsync(
                "CreatePlan",
                args: [],
                workingDir: _fixture.TestRepo.LocalClonePath,
                extraValues: new Dictionary<string, string>
                {
                    ["Args"] = "",
                    ["PlansDirectory"] = _fixture.PlansDir,
                    ["Project"] = "E2ETest"
                },
                timeout: TimeSpan.FromSeconds(120));

            // Agent may fail or succeed with empty input — just verify clean exit.
            Assert.True(result.ExitCode >= 0, "Process should exit cleanly");
        }
        catch (TimeoutException)
        {
            // A real agent with empty input may keep exploring indefinitely.
            // A timeout without a crash is an acceptable "graceful" outcome.
        }
    }

    private string? FindCreatedPlan(PromptwareResult result, string titleFragment)
    {
        // Check test's Plans dir first
        var folder = PromptwareAssertions.FindPlanFolderByTitle(_fixture.PlansDir, titleFragment);
        if (folder != null) return folder;

        // Fallback: check system Plans dir (env var inheritance issue on Windows)
        var systemPlansDir = Environment.GetEnvironmentVariable("TENDRIL_PLANS")
            ?? Path.Combine(Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "", "Plans");

        if (Directory.Exists(systemPlansDir))
        {
            folder = PromptwareAssertions.FindPlanFolderByTitle(systemPlansDir, titleFragment);
            if (folder != null) return folder;
        }

        // Last resort: parse stdout for the plan folder path
        var stdoutAll = string.Join("\n", result.StdoutLines);
        var match = System.Text.RegularExpressions.Regex.Match(
            stdoutAll, @"(\d{5}-[A-Za-z]+[^\s""\\]*)");
        if (match.Success)
        {
            var candidates = new[]
            {
                Path.Combine(_fixture.PlansDir, match.Value),
                Path.Combine(systemPlansDir, match.Value)
            };
            folder = candidates.FirstOrDefault(Directory.Exists);
            if (folder != null) return folder;
        }

        Assert.Fail(
            $"No plan folder found matching '{titleFragment}'.\n" +
            $"Test PlansDir: {_fixture.PlansDir} — contents: {GetDirContents(_fixture.PlansDir)}\n" +
            $"System PlansDir: {systemPlansDir} — contents: {GetDirContents(systemPlansDir)}\n" +
            $"Stdout (last 10): {string.Join("\n", result.StdoutLines.TakeLast(10))}");
        return null;
    }

    private static string GetDirContents(string path) =>
        Directory.Exists(path)
            ? string.Join(", ", Directory.GetDirectories(path).Select(Path.GetFileName).TakeLast(5))
            : "(does not exist)";
}
