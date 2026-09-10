using System;
using System.Collections.Generic;
using Ivy.Tendril.Apps.Chat;
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
