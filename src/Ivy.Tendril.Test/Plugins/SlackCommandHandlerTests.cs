using Ivy.Tendril.Plugins;
using Ivy.Tendril.Plugins.Slack;

namespace Ivy.Tendril.Test.Plugins;

public class SlackCommandHandlerTests
{
    private class FakeTendrilApi : ITendrilApi
    {
        public List<(string Description, string? Project)> CreatedPlans { get; } = [];
        public List<string> ExecutedPlans { get; } = [];
        public TendrilJobStatus? Job { get; set; }
        public List<TendrilPlanSummary> Plans { get; set; } = [];
        public List<string> Projects { get; set; } = [];

        public string StartCreatePlan(string description, string? project = null)
        {
            CreatedPlans.Add((description, project));
            return "job-1";
        }

        public string StartExecutePlan(string planId, string? note = null)
        {
            ExecutedPlans.Add(planId);
            return "job-2";
        }

        public TendrilJobStatus? GetJob(string jobId) => Job;

        public IReadOnlyList<TendrilPlanSummary> ListPlans(string? state = null, string? project = null, int limit = 20) =>
            Plans.Where(p => state == null || string.Equals(p.State, state, StringComparison.OrdinalIgnoreCase)).ToList();

        public IReadOnlyList<string> ListProjects() => Projects;
    }

    [Theory]
    [InlineData("")]
    [InlineData("help")]
    [InlineData("bogus command")]
    public void Execute_HelpOrUnknown_ReturnsHelp(string text)
    {
        var reply = SlackCommandHandler.Execute(text, new FakeTendrilApi());
        Assert.Contains("Tendril commands", reply);
    }

    [Fact]
    public void Execute_New_StartsCreatePlanJob()
    {
        var api = new FakeTendrilApi();
        var reply = SlackCommandHandler.Execute("new Add dark mode to settings", api);

        var created = Assert.Single(api.CreatedPlans);
        Assert.Equal("Add dark mode to settings", created.Description);
        Assert.Null(created.Project);
        Assert.Contains("job-1", reply);
    }

    [Fact]
    public void Execute_NewWithProject_PassesProject()
    {
        var api = new FakeTendrilApi();
        SlackCommandHandler.Execute("new project:WebApp Fix login redirect", api);

        var created = Assert.Single(api.CreatedPlans);
        Assert.Equal("Fix login redirect", created.Description);
        Assert.Equal("WebApp", created.Project);
    }

    [Fact]
    public void Execute_Run_ExecutesPlan()
    {
        var api = new FakeTendrilApi();
        var reply = SlackCommandHandler.Execute("run 00042", api);

        Assert.Equal("00042", Assert.Single(api.ExecutedPlans));
        Assert.Contains("job-2", reply);
    }

    [Fact]
    public void Execute_RunWithoutId_ReturnsUsage()
    {
        var api = new FakeTendrilApi();
        var reply = SlackCommandHandler.Execute("run", api);

        Assert.Empty(api.ExecutedPlans);
        Assert.Contains("Usage", reply);
    }

    [Fact]
    public void Execute_Plans_ListsPlans()
    {
        var api = new FakeTendrilApi
        {
            Plans =
            [
                new TendrilPlanSummary("00001", "First plan", "Draft", "WebApp", "Feature"),
                new TendrilPlanSummary("00002", "Second plan", "Done", null, null)
            ]
        };
        var reply = SlackCommandHandler.Execute("plans", api);

        Assert.Contains("First plan", reply);
        Assert.Contains("Second plan", reply);
    }

    [Fact]
    public void Execute_PlansWithState_Filters()
    {
        var api = new FakeTendrilApi
        {
            Plans =
            [
                new TendrilPlanSummary("00001", "First plan", "Draft", null, null),
                new TendrilPlanSummary("00002", "Second plan", "Done", null, null)
            ]
        };
        var reply = SlackCommandHandler.Execute("plans draft", api);

        Assert.Contains("First plan", reply);
        Assert.DoesNotContain("Second plan", reply);
    }

    [Fact]
    public void Execute_Status_ShowsJob()
    {
        var api = new FakeTendrilApi
        {
            Job = new TendrilJobStatus("job-9", "CreatePlan", "Running", "Working on it", null)
        };
        var reply = SlackCommandHandler.Execute("status job-9", api);

        Assert.Contains("job-9", reply);
        Assert.Contains("Running", reply);
    }

    [Fact]
    public void Execute_StatusUnknownJob_ReportsNotFound()
    {
        var reply = SlackCommandHandler.Execute("status nope", new FakeTendrilApi());
        Assert.Contains("not found", reply);
    }

    [Fact]
    public void Execute_Projects_ListsProjects()
    {
        var api = new FakeTendrilApi { Projects = ["WebApp", "Backend"] };
        var reply = SlackCommandHandler.Execute("projects", api);

        Assert.Contains("WebApp", reply);
        Assert.Contains("Backend", reply);
    }

    [Fact]
    public void Execute_ApiThrows_ReturnsErrorMessage()
    {
        var api = new ThrowingApi();
        var reply = SlackCommandHandler.Execute("run 42", api);
        Assert.Contains("boom", reply);
    }

    private class ThrowingApi : ITendrilApi
    {
        public string StartCreatePlan(string description, string? project = null) => throw new InvalidOperationException("boom");
        public string StartExecutePlan(string planId, string? note = null) => throw new InvalidOperationException("boom");
        public TendrilJobStatus? GetJob(string jobId) => throw new InvalidOperationException("boom");
        public IReadOnlyList<TendrilPlanSummary> ListPlans(string? state = null, string? project = null, int limit = 20) => throw new InvalidOperationException("boom");
        public IReadOnlyList<string> ListProjects() => throw new InvalidOperationException("boom");
    }
}
