using System.Reactive.Subjects;
using Ivy.Core.Apps;
using Ivy.Tendril.AppShell;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Test.TestHelpers;

namespace Ivy.Tendril.Test.AppShell;

public class SidebarMenuGenerationTests
{
    private class FakeAppRepository(MenuItem[] items) : IAppRepository
    {
        public IObservable<Unit> Reloaded => new Subject<Unit>();
        public IObservable<IReadOnlySet<string>> AppsRefreshRequested => new Subject<IReadOnlySet<string>>();
        public MenuItem[] GetMenuItems() => items;
        public AppDescriptor GetAppOrDefault(string? id) => new() { Id = id ?? "", Title = id ?? "", Group = [], IsVisible = true };
        public AppDescriptor? GetApp(string id) => null;
        public AppDescriptor? GetApp(Type type) => null;
    }

    private class TestConfigService : StubConfigService
    {
        public TendrilSettings CustomSettings { get; set; } = new();
        public new TendrilSettings Settings => CustomSettings;
    }

    [Theory]
    [InlineData("claude", Icons.ClaudeCode, "Claude Code")]
    [InlineData("copilot", Icons.Copilot, "Copilot")]
    [InlineData("codex", Icons.OpenAI, "Codex")]
    [InlineData("antigravity", Icons.Antigravity, "Antigravity")]
    [InlineData("opencode", Icons.OpenCode, "OpenCode")]
    [InlineData("ivy", Icons.IvyCorner, "Ivy Agent")]
    [InlineData("berget", Icons.ChevronUp, "Berget AI")]
    [InlineData("gemini", Icons.Gemini, "Agent")]
    public void BrandAgentItem_RebrandsAgentMenuItem_ForSupportedCodingAgents(
        string agentId, Icons expectedIcon, string expectedLabel)
    {
        var item = MenuItem.Default("Agent").Tag("agent").Icon(Icons.Terminal);
        var runner = TestAgentRunner.Create();
        var config = new TestConfigService();

        var branded = TendrilAppShell.BrandAgentItem(item, agentId, runner, config);

        Assert.Equal(expectedLabel, branded.Label);
        Assert.Equal(expectedIcon, branded.Icon);
        Assert.Equal("agent", branded.Tag);
    }

    [Fact]
    public void BrandAgentItem_UnknownOrEmptyAgent_FallsBackToDefaultLabelAndIcon()
    {
        var item = MenuItem.Default("Agent").Tag("agent").Icon(Icons.Terminal);
        var runner = TestAgentRunner.Create();
        var config = new TestConfigService();

        var brandedEmpty = TendrilAppShell.BrandAgentItem(item, "", runner, config);
        Assert.Equal(AgentBranding.DefaultLabel, brandedEmpty.Label);
        Assert.Equal(AgentBranding.DefaultIcon, brandedEmpty.Icon);

        var brandedUnknown = TendrilAppShell.BrandAgentItem(item, "nonexistent-agent-id", runner, config);
        Assert.Equal(AgentBranding.DefaultLabel, brandedUnknown.Label);
        Assert.Equal(AgentBranding.DefaultIcon, brandedUnknown.Icon);
    }

    [Fact]
    public void BrandAgentItem_NonAgentItem_IsUntouched()
    {
        var item = MenuItem.Default("Drafts").Tag("drafts").Icon(Icons.FileText);
        var runner = TestAgentRunner.Create();
        var config = new TestConfigService();

        var branded = TendrilAppShell.BrandAgentItem(item, "claude", runner, config);

        Assert.Equal("Drafts", branded.Label);
        Assert.Equal(Icons.FileText, branded.Icon);
        Assert.Equal("drafts", branded.Tag);
    }

    [Fact]
    public void BrandAgentItem_RebrandsNestedChildrenRecursively()
    {
        var childAgent = MenuItem.Default("Agent").Tag("agent").Icon(Icons.Terminal);
        var parent = MenuItem.Default("Coding Tools").Tag("tools").Icon(Icons.Wrench).Children(childAgent);
        var runner = TestAgentRunner.Create();
        var config = new TestConfigService();

        var branded = TendrilAppShell.BrandAgentItem(parent, "claude", runner, config);

        Assert.Equal("Coding Tools", branded.Label);
        Assert.NotNull(branded.Children);
        Assert.Single(branded.Children);
        Assert.Equal("Claude Code", branded.Children[0].Label);
        Assert.Equal(Icons.ClaudeCode, branded.Children[0].Icon);
    }

    [Fact]
    public void AddBadge_AttachesBadge_WhenCountGreaterThanZero()
    {
        var item = MenuItem.Default("Drafts").Tag("drafts").Icon(Icons.FileText);
        var badges = new Dictionary<string, int> { ["drafts"] = 5 };

        var badged = TendrilAppShell.AddBadge(item, badges);

        Assert.Equal("5", badged.Badge);
    }

    [Fact]
    public void AddBadge_DoesNotAttachBadge_WhenCountZeroOrMissing()
    {
        var item = MenuItem.Default("Drafts").Tag("drafts").Icon(Icons.FileText);
        var badges = new Dictionary<string, int> { ["drafts"] = 0, ["review"] = 3 };

        var badged = TendrilAppShell.AddBadge(item, badges);

        Assert.Null(badged.Badge);
    }

    [Fact]
    public void AddBadge_AttachesBadgeToNestedChildren()
    {
        var childDrafts = MenuItem.Default("Drafts").Tag("drafts").Icon(Icons.FileText);
        var childReview = MenuItem.Default("Review").Tag("review").Icon(Icons.Eye);
        var parent = MenuItem.Default("Plans").Tag("plans").Children(childDrafts, childReview);
        var badges = new Dictionary<string, int> { ["drafts"] = 2, ["review"] = 4 };

        var badged = TendrilAppShell.AddBadge(parent, badges);

        Assert.NotNull(badged.Children);
        Assert.Equal(2, badged.Children.Length);
        Assert.Equal("2", badged.Children[0].Badge);
        Assert.Equal("4", badged.Children[1].Badge);
    }

    [Fact]
    public void BuildMenuItems_CombinesBadgesAndAgentBranding()
    {
        var repoItems = new[]
        {
            MenuItem.Default("Drafts").Tag("drafts").Icon(Icons.FileText),
            MenuItem.Default("Review").Tag("review").Icon(Icons.Eye),
            MenuItem.Default("Jobs").Tag("jobs").Icon(Icons.Play),
            MenuItem.Default("Agent").Tag("agent").Icon(Icons.Terminal)
        };

        var repo = new FakeAppRepository(repoItems);
        var runner = TestAgentRunner.Create();
        var config = new TestConfigService();
        config.CustomSettings.CodingAgent = "claude";

        var status = new TendrilProcessStatus
        {
            DraftCount = 3,
            ReviewCount = 1,
            JobCount = 0
        };

        var result = TendrilAppShell.BuildMenuItems(repo, status, config, runner);

        Assert.Equal(4, result.Length);

        // Drafts: badge 3
        Assert.Equal("Drafts", result[0].Label);
        Assert.Equal("3", result[0].Badge);

        // Review: badge 1
        Assert.Equal("Review", result[1].Label);
        Assert.Equal("1", result[1].Badge);

        // Jobs: count 0 so no badge
        Assert.Equal("Jobs", result[2].Label);
        Assert.Null(result[2].Badge);

        // Agent: rebranded to Claude Code
        Assert.Equal("Claude Code", result[3].Label);
        Assert.Equal(Icons.ClaudeCode, result[3].Icon);
    }
}
