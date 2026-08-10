using Ivy.Core;
using Ivy.Tendril.Apps.Views.Tabs;

namespace Ivy.Tendril.Test.Apps.Views.Tabs;

public class DetailsTabViewRevisionCellTests
{
    private static object[] BuildChildren(int count, Action onRevert)
    {
        var layout = DetailsTabView.BuildRevisionCell(count, onRevert);
        return ((IWidget)layout.Build()!).Children;
    }

    [Fact]
    public void BuildRevisionCell_AtRevisionOne_OmitsRevertButton()
    {
        var children = BuildChildren(1, () => { });

        Assert.Empty(children.OfType<Button>());
    }

    [Fact]
    public void BuildRevisionCell_AtRevisionZero_OmitsRevertButton()
    {
        // Defensive: a plan with no revisions on disk reports 0 and still has nothing to revert to.
        var children = BuildChildren(0, () => { });

        Assert.Empty(children.OfType<Button>());
    }

    [Fact]
    public void BuildRevisionCell_AtRevisionTwo_ShowsEnabledRevertButton()
    {
        var children = BuildChildren(2, () => { });

        var button = Assert.Single(children.OfType<Button>());
        Assert.False(button.Disabled);
        Assert.Equal("Revert to previous revision", button.Tooltip);
        Assert.Equal(Icons.Undo, button.Icon);
    }

    [Fact]
    public void BuildRevisionCell_AlwaysShowsRevisionNumber()
    {
        Assert.Equal("1", RenderedText(1));
        Assert.Equal("7", RenderedText(7));
    }

    [Fact]
    public async Task BuildRevisionCell_ButtonClick_InvokesRevertCallback()
    {
        var reverted = false;
        var children = BuildChildren(3, () => reverted = true);

        var button = Assert.Single(children.OfType<Button>());
        await ((IWidget)button).InvokeEventAsync(nameof(Button.OnClick), []);

        Assert.True(reverted);
    }

    private static string RenderedText(int count)
    {
        var children = BuildChildren(count, () => { });
        var text = Assert.Single(children.OfType<TextBuilder>());
        return Assert.IsType<TextBlock>(text.Build()).Content;
    }
}
