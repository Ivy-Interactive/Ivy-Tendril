using System;
using System.Collections.Generic;
using Ivy.Tendril.Apps.Chat;
using Ivy.Tendril.Services;
using Xunit;

namespace Ivy.Tendril.Test.Apps.Chat;

public class ChatAppSidebarListTests
{
    private static ChatSessionModel Session(string id, string title) =>
        new(id, title, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "claude", "opus", []);

    [Fact]
    public void BuildSidebarList_MapsSessionsToRowsAndRoutesSelectionThroughChatArgs()
    {
        var sessions = new List<ChatSessionModel> { Session("a", "First"), Session("b", "") };
        var searched = false;

        var list = ChatApp.BuildSidebarList(sessions, "a", new HashSet<string>(), new HashSet<string>(), () => searched = true);

        Assert.Equal("chat", list.AppId);
        Assert.Equal("Chats", list.Title);
        Assert.Equal("a", list.SelectedId);
        Assert.True(list.Searchable);
        Assert.Equal("Search chats", list.SearchLabel);
        Assert.Collection(list.Items,
            item => Assert.Equal(("a", "First"), (item.Id, item.Title)),
            item => Assert.Equal(("b", "New Chat"), (item.Id, item.Title)));

        var args = Assert.IsType<ChatAppArgs>(list.BuildSelectArgs("b"));
        Assert.Equal("b", args.SessionId);

        list.OnSearch!();
        Assert.True(searched);
    }

    [Fact]
    public void BuildRowBadges_FlagsWorkingAndUnseenCompletedSessionsOnly()
    {
        var generating = new HashSet<string> { "gen" };
        var completed = new HashSet<string> { "done", "current" };

        var working = ChatApp.BuildRowBadges(Session("gen", "x"), "current", generating, completed);
        var finished = ChatApp.BuildRowBadges(Session("done", "x"), "current", generating, completed);
        var active = ChatApp.BuildRowBadges(Session("current", "x"), "current", generating, completed);
        var idle = ChatApp.BuildRowBadges(Session("idle", "x"), "current", generating, completed);

        Assert.Equal(("Working", "warning"), (working![0].Label, working[0].Kind));
        Assert.Equal(("Completed", "success"), (finished![0].Label, finished[0].Kind));
        Assert.Null(active);
        Assert.Null(idle);
    }
}
