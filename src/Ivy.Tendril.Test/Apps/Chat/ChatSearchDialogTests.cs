using System;
using System.Collections.Generic;
using Ivy.Tendril.Apps.Chat.Dialogs;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Widgets;
using Xunit;

namespace Ivy.Tendril.Test.Apps.Chat;

public class ChatSearchDialogTests
{
    private static ChatSessionModel CreateSession(
        string id,
        string title,
        DateTimeOffset updatedAt,
        string? kind = null) =>
        new(id, title, DateTimeOffset.UtcNow, updatedAt, "claude", "opus", [], Kind: kind);

    [Fact]
    public void BuildResultsSection_BuildsNonCollapsibleSectionWithTitlesTagsAndIcons()
    {
        var updated = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        var sessions = new List<ChatSessionModel>
        {
            CreateSession("s1", "Investigate Bug", updated, ChatSessionKinds.Chat),
            CreateSession("s2", "Shell Terminal", updated, ChatSessionKinds.Terminal),
            CreateSession("s3", "", updated)
        };

        var selected = "";
        var section = ChatSearchDialog.BuildResultsSection(sessions, id => selected = id);

        Assert.False(section.Collapsible);
        Assert.Equal(3, section.Items.Count);

        var first = section.Items[0];
        Assert.Equal("s1", first.Id);
        Assert.Equal("Investigate Bug", first.Title);
        Assert.Equal(updated.ToLocalTime().ToString("MMM d"), first.Tag);
        Assert.Null(first.Icon);

        var second = section.Items[1];
        Assert.Equal("s2", second.Id);
        Assert.Equal("Shell Terminal", second.Title);
        Assert.Equal("Terminal", second.Icon);

        var third = section.Items[2];
        Assert.Equal("s3", third.Id);
        Assert.Equal("New Chat", third.Title);

        section.OnSelectItem!.Invoke(new Event<ShellSidebarSection, string>("select", section, "s2"));
        Assert.Equal("s2", selected);
    }
}
