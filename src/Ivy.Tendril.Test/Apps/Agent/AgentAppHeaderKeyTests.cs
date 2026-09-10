using System;
using Ivy.Tendril.Apps.Agent;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Xunit;

namespace Ivy.Tendril.Test.Apps.Agent;

public class AgentAppHeaderKeyTests
{
    private static ChatSessionModel Session(string title) =>
        new("sess", title, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "claude", "opus", [], Kind: ChatSessionKinds.Terminal);

    private static JobItem Job(JobStatus status, string? message = null, string? planTitle = null) =>
        new() { Id = "job-1", Type = "CreatePlan", Status = status, StatusMessage = message, ReportedPlanTitle = planTitle };

    [Fact]
    public void HeaderKey_ChangesWhenTheHeaderContentChanges()
    {
        var baseline = AgentApp.HeaderKey(Session("Fix login"), [Job(JobStatus.Running, "cloning")]);

        Assert.Equal(baseline, AgentApp.HeaderKey(Session("Fix login"), [Job(JobStatus.Running, "cloning")]));
        Assert.NotEqual(baseline, AgentApp.HeaderKey(Session("Renamed"), [Job(JobStatus.Running, "cloning")]));
        Assert.NotEqual(baseline, AgentApp.HeaderKey(Session("Fix login"), [Job(JobStatus.Completed, "cloning")]));
        Assert.NotEqual(baseline, AgentApp.HeaderKey(Session("Fix login"), [Job(JobStatus.Running, "building")]));
        Assert.NotEqual(baseline, AgentApp.HeaderKey(Session("Fix login"), [Job(JobStatus.Running, "cloning", "Plan 12")]));
        Assert.NotEqual(baseline, AgentApp.HeaderKey(Session("Fix login"), []));
        Assert.NotEqual(baseline, AgentApp.HeaderKey(null, [Job(JobStatus.Running, "cloning")]));
    }
}
