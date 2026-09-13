using Ivy;
using Ivy.Core.Hooks;
using Ivy.Tendril.Apps.Plans.Dialogs;
using Xunit;

namespace Ivy.Tendril.Test.Apps.Plans.Dialogs;

public class PendingAnnotationsDialogTests
{
    private static List<Button> GetFooterButtons(Dialog dialog)
    {
        var footer = dialog.Children.OfType<DialogFooter>().FirstOrDefault();
        Assert.NotNull(footer);

        var buttons = new List<Button>();
        foreach (var child in footer.Children)
        {
            if (child is Button btn)
            {
                buttons.Add(btn);
            }
            else if (child is LayoutView layout)
            {
                var elementsField = typeof(LayoutView).GetField("_elements", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (elementsField?.GetValue(layout) is System.Collections.IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                    {
                        var content = item?.GetType().GetProperty("Content")?.GetValue(item);
                        if (content is Button layoutBtn)
                        {
                            buttons.Add(layoutBtn);
                        }
                    }
                }
            }
        }
        return buttons;
    }

    [Fact]
    public void Build_WhenClosed_ReturnsNull()
    {
        var dialogOpen = new State<bool>(false);
        var updated = false;
        var updatedAndExecuted = false;
        var discardedAndExecuted = false;

        var dialog = new PendingAnnotationsDialog(
            dialogOpen,
            annotationCount: 1,
            answeredQuestionCount: 1,
            () => updated = true,
            () => updatedAndExecuted = true,
            () => discardedAndExecuted = true);

        var result = dialog.Build();

        Assert.Null(result);
        Assert.False(updated);
        Assert.False(updatedAndExecuted);
        Assert.False(discardedAndExecuted);
    }

    [Fact]
    public void Build_WhenOpen_ConfiguresUpdatePlanAndExecuteButtonWithShortcut()
    {
        var dialogOpen = new State<bool>(true);
        var dialog = new PendingAnnotationsDialog(
            dialogOpen,
            annotationCount: 1,
            answeredQuestionCount: 0,
            () => { },
            () => { },
            () => { });

        var result = dialog.Build();

        Assert.NotNull(result);
        var dialogWidget = Assert.IsType<Dialog>(result);
        var buttons = GetFooterButtons(dialogWidget);

        var button = buttons.FirstOrDefault(b => b.Title == "Update Plan & Execute");
        Assert.NotNull(button);
        Assert.Equal("Ctrl+Enter", button.ShortcutKey);
    }

    [Fact]
    public async Task Build_WhenOpen_UpdatePlanAndExecuteButtonInvokesCallback()
    {
        var dialogOpen = new State<bool>(true);
        var updated = false;
        var updatedAndExecuted = false;
        var discardedAndExecuted = false;

        var dialog = new PendingAnnotationsDialog(
            dialogOpen,
            annotationCount: 2,
            answeredQuestionCount: 1,
            () => updated = true,
            () => updatedAndExecuted = true,
            () => discardedAndExecuted = true);

        var result = dialog.Build();

        Assert.NotNull(result);
        var dialogWidget = Assert.IsType<Dialog>(result);
        var buttons = GetFooterButtons(dialogWidget);

        var button = buttons.FirstOrDefault(b => b.Title == "Update Plan & Execute");
        Assert.NotNull(button);
        Assert.NotNull(button.OnClick);

        await button.OnClick.Invoke(new Event<Button>("click", button));

        Assert.False(dialogOpen.Value);
        Assert.False(updated);
        Assert.True(updatedAndExecuted);
        Assert.False(discardedAndExecuted);
    }

    [Fact]
    public async Task Build_WhenOpen_UpdatePlanButtonInvokesCallback()
    {
        var dialogOpen = new State<bool>(true);
        var updated = false;
        var updatedAndExecuted = false;
        var discardedAndExecuted = false;

        var dialog = new PendingAnnotationsDialog(
            dialogOpen,
            annotationCount: 2,
            answeredQuestionCount: 1,
            () => updated = true,
            () => updatedAndExecuted = true,
            () => discardedAndExecuted = true);

        var result = dialog.Build();

        Assert.NotNull(result);
        var dialogWidget = Assert.IsType<Dialog>(result);
        var buttons = GetFooterButtons(dialogWidget);

        var button = buttons.FirstOrDefault(b => b.Title == "Update Plan");
        Assert.NotNull(button);
        Assert.NotNull(button.OnClick);

        await button.OnClick.Invoke(new Event<Button>("click", button));

        Assert.False(dialogOpen.Value);
        Assert.True(updated);
        Assert.False(updatedAndExecuted);
        Assert.False(discardedAndExecuted);
    }

    [Fact]
    public async Task Build_WhenOpen_DiscardAndExecuteButtonInvokesCallback()
    {
        var dialogOpen = new State<bool>(true);
        var updated = false;
        var updatedAndExecuted = false;
        var discardedAndExecuted = false;

        var dialog = new PendingAnnotationsDialog(
            dialogOpen,
            annotationCount: 1,
            answeredQuestionCount: 0,
            () => updated = true,
            () => updatedAndExecuted = true,
            () => discardedAndExecuted = true);

        var result = dialog.Build();

        Assert.NotNull(result);
        var dialogWidget = Assert.IsType<Dialog>(result);
        var buttons = GetFooterButtons(dialogWidget);

        var button = buttons.FirstOrDefault(b => b.Title == "Discard Annotations & Execute");
        Assert.NotNull(button);
        Assert.NotNull(button.OnClick);

        await button.OnClick.Invoke(new Event<Button>("click", button));

        Assert.False(dialogOpen.Value);
        Assert.False(updated);
        Assert.False(updatedAndExecuted);
        Assert.True(discardedAndExecuted);
    }

    [Fact]
    public async Task Build_WhenOpen_CancelButtonClosesDialogWithoutInvokingCallbacks()
    {
        var dialogOpen = new State<bool>(true);
        var updated = false;
        var updatedAndExecuted = false;
        var discardedAndExecuted = false;

        var dialog = new PendingAnnotationsDialog(
            dialogOpen,
            annotationCount: 1,
            answeredQuestionCount: 0,
            () => updated = true,
            () => updatedAndExecuted = true,
            () => discardedAndExecuted = true);

        var result = dialog.Build();

        Assert.NotNull(result);
        var dialogWidget = Assert.IsType<Dialog>(result);
        var buttons = GetFooterButtons(dialogWidget);

        var button = buttons.FirstOrDefault(b => b.Title == "Cancel");
        Assert.NotNull(button);
        Assert.NotNull(button.OnClick);

        await button.OnClick.Invoke(new Event<Button>("click", button));

        Assert.False(dialogOpen.Value);
        Assert.False(updated);
        Assert.False(updatedAndExecuted);
        Assert.False(discardedAndExecuted);
    }
}
