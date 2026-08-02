using Ivy.Tendril.Apps.Jobs.Dialogs;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Plans;
using Xunit;

namespace Ivy.Tendril.Test;

public class RerunJobDialogTests
{
    private sealed class MockPlanReaderService(string plansDirectory) : IPlanReaderService
    {
        public string PlansDirectory => plansDirectory;
        public bool IsDatabaseReady => false;
        public void MigratePlans() { }
        public void RecoverStuckPlans() { }
        public List<PlanFile> GetPlans(PlanStatus? statusFilter = null) => [];
        public PlanFile? GetPlanByFolder(string folderPath) => null;
        public List<PlanFile> GetIceboxPlans() => [];
        public void TransitionState(string folderName, PlanStatus newState) { }
        public HashSet<string> GetFailedVerificationNames(string folderName) => [];
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void BuildRerunArgs_NoFeedback_ReturnsOriginalArgs(string? feedback)
    {
        var original = new ExecutePlanArgs("/plans/00001-Test");

        var result = RerunJobDialog.BuildRerunArgs(original, feedback);

        Assert.Same(original, result);
    }

    [Fact]
    public void BuildRerunArgs_ExecutePlanWithFeedback_BecomesRetryWithChangeRequest()
    {
        var original = new ExecutePlanArgs("/plans/00001-Test");

        var result = RerunJobDialog.BuildRerunArgs(original, "fix the readme");

        var retry = Assert.IsType<RetryPlanArgs>(result);
        Assert.Equal("/plans/00001-Test", retry.FolderPath);
        Assert.Equal("fix the readme", retry.ChangeRequest);
    }

    [Fact]
    public void BuildRerunArgs_RetryPlanWithFeedback_KeepsRetryWithNewChangeRequest()
    {
        var original = new RetryPlanArgs("/plans/00001-Test", "old request");

        var result = RerunJobDialog.BuildRerunArgs(original, "new request");

        var retry = Assert.IsType<RetryPlanArgs>(result);
        Assert.Equal("/plans/00001-Test", retry.FolderPath);
        Assert.Equal("new request", retry.ChangeRequest);
    }

    [Fact]
    public void BuildRerunArgs_UpdatePlanWithFeedback_BecomesUpdateWithInstructions()
    {
        var original = new UpdatePlanArgs("/plans/00001-Test", "old instructions");

        var result = RerunJobDialog.BuildRerunArgs(original, "new instructions");

        var update = Assert.IsType<UpdatePlanArgs>(result);
        Assert.Equal("/plans/00001-Test", update.FolderPath);
        Assert.Equal("new instructions", update.Instructions);
    }

    [Fact]
    public void BuildRerunArgs_UnsupportedTypeWithFeedback_ReturnsOriginalArgs()
    {
        var original = new CreatePrArgs("/plans/00001-Test");

        var result = RerunJobDialog.BuildRerunArgs(original, "some feedback");

        Assert.Same(original, result);
    }

    [Fact]
    public void BuildRerunArgs_CreatePlanWithPlanFolderAndFeedback_BecomesRetryPlan()
    {
        var original = new CreatePlanArgs("Create a login feature", "MyProject");

        var result = RerunJobDialog.BuildRerunArgs(original, "fix authentication bug", "/plans/00042-LoginFeature");

        var retry = Assert.IsType<RetryPlanArgs>(result);
        Assert.Equal("/plans/00042-LoginFeature", retry.FolderPath);
        Assert.Equal("fix authentication bug", retry.ChangeRequest);
    }

    [Fact]
    public void BuildRerunArgs_CreatePlanWithPlanFolderNoFeedback_BecomesExecutePlan()
    {
        var original = new CreatePlanArgs("Create a login feature", "MyProject");

        var result = RerunJobDialog.BuildRerunArgs(original, "", "/plans/00042-LoginFeature");

        var execute = Assert.IsType<ExecutePlanArgs>(result);
        Assert.Equal("/plans/00042-LoginFeature", execute.FolderPath);
    }

    [Fact]
    public void BuildRerunArgs_CreatePlanWithoutPlanFolder_ReturnsOriginalCreatePlanArgs()
    {
        var original = new CreatePlanArgs("Create a login feature", "MyProject");

        var result = RerunJobDialog.BuildRerunArgs(original, "some feedback", null);

        Assert.Same(original, result);
    }

    [Theory]
    [InlineData(typeof(ExecutePlanArgs), true)]
    [InlineData(typeof(RetryPlanArgs), true)]
    [InlineData(typeof(UpdatePlanArgs), true)]
    [InlineData(typeof(ExpandPlanArgs), false)]
    [InlineData(typeof(CreatePrArgs), false)]
    public void SupportsFeedback_MatchesJobType(Type argsType, bool expected)
    {
        JobArgsBase args = argsType switch
        {
            _ when argsType == typeof(ExecutePlanArgs) => new ExecutePlanArgs("/plans/00001-Test"),
            _ when argsType == typeof(RetryPlanArgs) => new RetryPlanArgs("/plans/00001-Test", "req"),
            _ when argsType == typeof(UpdatePlanArgs) => new UpdatePlanArgs("/plans/00001-Test"),
            _ when argsType == typeof(ExpandPlanArgs) => new ExpandPlanArgs("/plans/00001-Test"),
            _ => new CreatePrArgs("/plans/00001-Test")
        };

        Assert.Equal(expected, RerunJobDialog.SupportsFeedback(args));
    }

    [Fact]
    public void SupportsFeedback_JobItem_CreatePlanWithReportedId_ReturnsTrue()
    {
        var job = new JobItem
        {
            Id = "00001",
            Type = "CreatePlan",
            TypedArgs = new CreatePlanArgs("Test", "Project"),
            ReportedPlanId = "00042",
            PlanFile = "00042-TestPlan"
        };

        Assert.True(RerunJobDialog.SupportsFeedback(job, null));
    }

    [Fact]
    public void ResolvePlanFolder_PlanIdMatchesFolder_ReturnsAbsolutePath()
    {
        using var tempDir = new TempDirectoryFixture("rerun-job-dialog-test");
        var planFolder = Path.Combine(tempDir.Path, "00038-FixRustyDocsGeneratorOutputAndAddWidgetSampleAppsWithAligned");
        Directory.CreateDirectory(planFolder);

        var job = new JobItem
        {
            Id = "00001",
            Type = "ExecutePlan",
            ReportedPlanId = "00038",
            PlanFile = "00038-FixRustyDocsGeneratorOutputAndAddWidgetSampleAppsWithAligned"
        };
        var planService = new MockPlanReaderService(tempDir.Path);

        var result = RerunJobDialog.ResolvePlanFolder(job, planService);

        Assert.NotNull(result);
        Assert.True(Path.IsPathRooted(result), $"Expected rooted path but got: {result}");
        Assert.Equal(planFolder, result);
    }

    [Fact]
    public void ResolvePlanFolder_PlanFileWithoutExistingDirectory_ReturnsAbsolutePath()
    {
        using var tempDir = new TempDirectoryFixture("rerun-job-dialog-test");

        var job = new JobItem
        {
            Id = "00001",
            Type = "ExecutePlan",
            ReportedPlanId = "00099",
            PlanFile = "00099-Missing"
        };
        var planService = new MockPlanReaderService(tempDir.Path);

        var result = RerunJobDialog.ResolvePlanFolder(job, planService);

        Assert.NotNull(result);
        Assert.True(Path.IsPathRooted(result), $"Expected rooted path but got: {result}");
        Assert.StartsWith(tempDir.Path, result);
    }

    [Fact]
    public void BuildRerunArgs_CreatePlanWithAbsolutePlanFolder_PassesPathThrough()
    {
        using var tempDir = new TempDirectoryFixture("rerun-job-dialog-test");
        var planFolder = Path.Combine(tempDir.Path, "00042-LoginFeature");
        Directory.CreateDirectory(planFolder);

        var original = new CreatePlanArgs("Create a login feature", "MyProject");

        var result = RerunJobDialog.BuildRerunArgs(original, "", planFolder);

        var execute = Assert.IsType<ExecutePlanArgs>(result);
        Assert.True(Path.IsPathRooted(execute.FolderPath), $"Expected rooted path but got: {execute.FolderPath}");
        Assert.Equal(planFolder, execute.FolderPath);
    }
}
