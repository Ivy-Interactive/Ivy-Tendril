using System;
using System.Collections.Generic;
using System.IO;
using Ivy.Tendril.Apps.Chat;
using Ivy.Tendril.Apps.Icebox;
using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Apps.Review;
using Ivy.Tendril.AppShell.Dialogs;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Widgets;
using Xunit;

namespace Ivy.Tendril.Test.Apps.Chat;

public class ChatAppSidebarListTests
{
    private static ChatSessionModel Session(string id, string title, string? kind = null) =>
        new(id, title, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "claude", "opus", [], Kind: kind);

    [Fact]
    public void BuildSidebarList_MapsSessionsToRowsAndRoutesSelectionThroughChatArgs()
    {
        var sessions = new List<ChatSessionModel> { Session("a", "First"), Session("b", "") };
        var searched = false;

        var list = ChatApp.BuildSidebarList(sessions, "a", new HashSet<string> { "b" }, new HashSet<string>(), () => searched = true);

        Assert.Equal("chat", list.AppId);
        Assert.Equal("Chats", list.Title);
        Assert.Equal("a", list.SelectedId);
        Assert.True(list.Searchable);
        Assert.Equal("Search chats", list.SearchLabel);
        Assert.Collection(list.Items,
            item => Assert.Equal(("a", "First", (string?)null), (item.Id, item.Title, item.State)),
            item => Assert.Equal(("b", "New Chat", "working"), (item.Id, item.Title, item.State)));
        Assert.All(list.Items, item => Assert.Null(item.Badges));

        var args = Assert.IsType<ChatAppArgs>(list.BuildSelectArgs("b"));
        Assert.Equal("b", args.SessionId);

        list.OnSearch!();
        Assert.True(searched);
        Assert.Null(list.OnNew);
        Assert.Null(list.NewLabel);
    }

    [Fact]
    public void BuildSidebarList_FoldsIntoTheCollapsedRailMenu()
    {
        var list = ChatApp.BuildSidebarList([Session("a", "First")], null, new HashSet<string>(), new HashSet<string>(), () => { });

        Assert.True(list.CollapsedMenu);
    }

    [Fact]
    public void BuildSidebarList_MarksTerminalSessionsAndExposesNewChat()
    {
        var sessions = new List<ChatSessionModel>
        {
            Session("chat", "Chat", ChatSessionKinds.Chat),
            Session("term", "Terminal", ChatSessionKinds.Terminal)
        };
        var started = false;

        var list = ChatApp.BuildSidebarList(sessions, null, new HashSet<string>(), new HashSet<string>(), () => { }, () => started = true);

        Assert.Collection(list.Items,
            item => Assert.Null(item.Icon),
            item => Assert.Equal("Terminal", item.Icon));
        Assert.Equal("New chat", list.NewLabel);
        list.OnNew!();
        Assert.True(started);
    }

    [Fact]
    public void BuildRowState_FlagsWorkingAndUnseenCompletedSessionsOnly()
    {
        var generating = new HashSet<string> { "gen" };
        var completed = new HashSet<string> { "done", "current" };

        Assert.Equal("working", ChatApp.BuildRowState(Session("gen", "x"), "current", generating, completed));
        Assert.Equal("completed", ChatApp.BuildRowState(Session("done", "x"), "current", generating, completed));
        Assert.Null(ChatApp.BuildRowState(Session("current", "x"), "current", generating, completed));
        Assert.Null(ChatApp.BuildRowState(Session("idle", "x"), "current", generating, completed));
    }

    [Fact]
    public void ToJobDto_CarriesTheBackendColorOfKnownJobTypes()
    {
        var execute = ChatApp.ToJobDto(new JobItem { Id = "00148", Type = Constants.JobTypes.ExecutePlan, Status = JobStatus.Completed, ReportedPlanId = "00059" });
        var custom = ChatApp.ToJobDto(new JobItem { Id = "00149", Type = "Promptware", Status = JobStatus.Running });

        Assert.Equal(("00148", "ExecutePlan", "Completed", "00059", "Blue"), (execute.Id, execute.Type, execute.Status, execute.PlanId, execute.TypeColor));
        Assert.Null(custom.TypeColor);
    }

    [Fact]
    public void ToJobDto_ResolvesPlanIdAndFallbackTitle_WhenReportedPlanIdIsNull()
    {
        var job = new JobItem
        {
            Id = "00150",
            Type = Constants.JobTypes.ExecutePlan,
            Status = JobStatus.Running,
            PlanFile = "01579-TestPlan"
        };

        var dto = ChatApp.ToJobDto(job);

        Assert.Equal("01579", dto.PlanId);
        Assert.Equal("TestPlan", dto.PlanTitle);
    }

    [Fact]
    public void ToJobDto_ResolvesHumanReadablePlanTitle_WhenPlanServiceIsProvided()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), "01579-TestPlan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        try
        {
            var metadata = new PlanMetadata(
                1579, "test-project", "Feature", "Human Readable Plan Title", PlanStatus.Executing,
                [], [], [], [], [], [], DateTime.UtcNow, DateTime.UtcNow, null, null);
            var planFile = new PlanFile(metadata, "", tempFolder, "");
            var planService = new FakePlanReaderService
            {
                Plans = [planFile]
            };
            var job = new JobItem
            {
                Id = "00151",
                Type = Constants.JobTypes.ExecutePlan,
                Status = JobStatus.Running,
                PlanFile = Path.GetFileName(tempFolder)
            };

            var dto = ChatApp.ToJobDto(job, planService);

            Assert.Equal("01579", dto.PlanId);
            Assert.Equal("Human Readable Plan Title", dto.PlanTitle);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder);
        }
    }

    [Fact]
    public void ToJobDto_ClearsPlanId_WhenPlanIsCompleted()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), "01580-CompletedPlan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        try
        {
            var metadata = new PlanMetadata(
                1580, "test-project", "Feature", "Completed Plan Title", PlanStatus.Completed,
                [], [], [], [], [], [], DateTime.UtcNow, DateTime.UtcNow, null, null);
            var planFile = new PlanFile(metadata, "", tempFolder, "");
            var planService = new FakePlanReaderService { Plans = [planFile] };
            var job = new JobItem
            {
                Id = "00152",
                Type = Constants.JobTypes.ExecutePlan,
                Status = JobStatus.Completed,
                PlanFile = Path.GetFileName(tempFolder)
            };

            var dto = ChatApp.ToJobDto(job, planService);

            Assert.Null(dto.PlanId);
            Assert.Equal("Completed Plan Title", dto.PlanTitle);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder);
        }
    }

    [Fact]
    public void ToJobDto_ClearsPlanId_WhenPlanIsSkipped()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), "01581-SkippedPlan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        try
        {
            var metadata = new PlanMetadata(
                1581, "test-project", "Feature", "Skipped Plan Title", PlanStatus.Skipped,
                [], [], [], [], [], [], DateTime.UtcNow, DateTime.UtcNow, null, null);
            var planFile = new PlanFile(metadata, "", tempFolder, "");
            var planService = new FakePlanReaderService { Plans = [planFile] };
            var job = new JobItem
            {
                Id = "00153",
                Type = Constants.JobTypes.ExecutePlan,
                Status = JobStatus.Completed,
                PlanFile = Path.GetFileName(tempFolder)
            };

            var dto = ChatApp.ToJobDto(job, planService);

            Assert.Null(dto.PlanId);
            Assert.Equal("Skipped Plan Title", dto.PlanTitle);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder);
        }
    }

    [Fact]
    public void ToJobDto_ClearsPlanId_WhenPlanDoesNotExist()
    {
        var planService = new FakePlanReaderService { Plans = [] };
        var job = new JobItem
        {
            Id = "00154",
            Type = Constants.JobTypes.ExecutePlan,
            Status = JobStatus.Running,
            PlanFile = "01582-MissingPlan"
        };

        var dto = ChatApp.ToJobDto(job, planService);

        Assert.Null(dto.PlanId);
        Assert.Equal("MissingPlan", dto.PlanTitle);
    }

    [Fact]
    public void ToJobDto_ClearsPlanId_WhenPlanFolderMissingOnDisk()
    {
        // The id prefix is load-bearing: FindPlan must resolve the plan by id before the
        // on-disk check is what clears PlanId, not an unresolved-plan fallback.
        var missingFolder = Path.Combine(Path.GetTempPath(), "01583-MissingFolderPlan-" + Guid.NewGuid().ToString("N"));
        var metadata = new PlanMetadata(
            1583, "test-project", "Feature", "Missing Folder Plan", PlanStatus.Draft,
            [], [], [], [], [], [], DateTime.UtcNow, DateTime.UtcNow, null, null);
        var planFile = new PlanFile(metadata, "", missingFolder, "");
        var planService = new FakePlanReaderService { Plans = [planFile] };
        var job = new JobItem
        {
            Id = "00155",
            Type = Constants.JobTypes.ExecutePlan,
            Status = JobStatus.Running,
            PlanFile = Path.GetFileName(missingFolder)
        };

        var dto = ChatApp.ToJobDto(job, planService);

        Assert.Null(dto.PlanId);
        Assert.Equal("Missing Folder Plan", dto.PlanTitle);
    }

    [Theory]
    [InlineData(PlanStatus.Draft)]
    [InlineData(PlanStatus.Review)]
    [InlineData(PlanStatus.Failed)]
    public void ToJobDto_RetainsPlanId_WhenPlanIsDraftOrReview(PlanStatus status)
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), "01584-ActivePlan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        try
        {
            var metadata = new PlanMetadata(
                1584, "test-project", "Feature", "Active Plan Title", status,
                [], [], [], [], [], [], DateTime.UtcNow, DateTime.UtcNow, null, null);
            var planFile = new PlanFile(metadata, "", tempFolder, "");
            var planService = new FakePlanReaderService { Plans = [planFile] };
            var job = new JobItem
            {
                Id = "00156",
                Type = Constants.JobTypes.ExecutePlan,
                Status = JobStatus.Running,
                PlanFile = Path.GetFileName(tempFolder)
            };

            var dto = ChatApp.ToJobDto(job, planService);

            Assert.Equal("01584", dto.PlanId);
            Assert.Equal("Active Plan Title", dto.PlanTitle);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder);
        }
    }

    [Fact]
    public void ResolveTarget_ReturnsNull_ForCompletedAndSkippedPlans()
    {
        PlanFile CreateTestPlan(PlanStatus status, string folder = "01585-TargetPlan") =>
            new(new PlanMetadata(1585, "test", "Feature", "Target Plan", status, [], [], [], [], [], [], DateTime.UtcNow, DateTime.UtcNow, null, null),
                "", "/tmp/" + folder, "");

        Assert.Null(PlanSearchDialog.ResolveTarget(CreateTestPlan(PlanStatus.Completed)));
        Assert.Null(PlanSearchDialog.ResolveTarget(CreateTestPlan(PlanStatus.Skipped)));

        var draftTarget = PlanSearchDialog.ResolveTarget(CreateTestPlan(PlanStatus.Draft));
        Assert.NotNull(draftTarget);
        Assert.Equal(typeof(PlansApp), draftTarget.Value.App);

        var blockedTarget = PlanSearchDialog.ResolveTarget(CreateTestPlan(PlanStatus.Blocked));
        Assert.NotNull(blockedTarget);
        Assert.Equal(typeof(PlansApp), blockedTarget.Value.App);

        var reviewTarget = PlanSearchDialog.ResolveTarget(CreateTestPlan(PlanStatus.Review));
        Assert.NotNull(reviewTarget);
        Assert.Equal(typeof(ReviewApp), reviewTarget.Value.App);

        var failedTarget = PlanSearchDialog.ResolveTarget(CreateTestPlan(PlanStatus.Failed));
        Assert.NotNull(failedTarget);
        Assert.Equal(typeof(ReviewApp), failedTarget.Value.App);

        var iceboxTarget = PlanSearchDialog.ResolveTarget(CreateTestPlan(PlanStatus.Icebox));
        Assert.NotNull(iceboxTarget);
        Assert.Equal(typeof(IceboxApp), iceboxTarget.Value.App);
        Assert.Null(iceboxTarget.Value.Args);
    }

    [Fact]
    public void ResolveModelAndEffort_FallBackWhenThePreferenceIsUnknown()
    {
        var models = new List<(string Id, string DisplayName)> { ("opus", "Opus"), ("sonnet", "Sonnet") };
        var efforts = new List<EffortOptionDto> { new("default", "Default"), new("max", "Max") };

        Assert.Equal("sonnet", ChatApp.ResolveModel(models, "Sonnet"));
        Assert.Equal("opus", ChatApp.ResolveModel(models, "gone"));
        Assert.Equal("opus", ChatApp.ResolveModel(models, null));
        Assert.Equal("max", ChatApp.ResolveEffort(efforts, "max"));
        Assert.Equal("default", ChatApp.ResolveEffort(efforts, "ultra"));
    }
}
