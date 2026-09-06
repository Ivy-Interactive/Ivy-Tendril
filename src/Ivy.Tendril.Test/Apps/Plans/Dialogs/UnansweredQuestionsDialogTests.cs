using Ivy;
using Ivy.Core.Hooks;
using Ivy.Tendril.Apps.Plans.Dialogs;
using Xunit;

namespace Ivy.Tendril.Test.Apps.Plans.Dialogs;

public class UnansweredQuestionsDialogTests
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
        var continued = false;
        var dialog = new UnansweredQuestionsDialog(dialogOpen, 3, () => continued = true);

        var result = dialog.Build();

        Assert.Null(result);
        Assert.False(continued);
    }

    [Fact]
    public void Build_WhenOpen_ConfiguresExecuteAnywayButtonWithShortcut()
    {
        var dialogOpen = new State<bool>(true);
        var dialog = new UnansweredQuestionsDialog(dialogOpen, 2, () => { });

        var result = dialog.Build();

        Assert.NotNull(result);
        var dialogWidget = Assert.IsType<Dialog>(result);
        var buttons = GetFooterButtons(dialogWidget);

        var executeButton = buttons.FirstOrDefault(b => b.Title == "Execute Anyway");
        Assert.NotNull(executeButton);
        Assert.Equal("Ctrl+Enter", executeButton.ShortcutKey);
    }

    [Fact]
    public async Task Build_WhenOpen_ExecuteAnywayButtonInvokesContinue()
    {
        var dialogOpen = new State<bool>(true);
        var continued = false;
        var dialog = new UnansweredQuestionsDialog(dialogOpen, 1, () => continued = true);

        var result = dialog.Build();

        Assert.NotNull(result);
        var dialogWidget = Assert.IsType<Dialog>(result);
        var buttons = GetFooterButtons(dialogWidget);

        var executeButton = buttons.FirstOrDefault(b => b.Title == "Execute Anyway");
        Assert.NotNull(executeButton);
        Assert.NotNull(executeButton.OnClick);

        await executeButton.OnClick.Invoke(new Event<Button>("click", executeButton));

        Assert.False(dialogOpen.Value);
        Assert.True(continued);
    }

    [Fact]
    public async Task Build_WhenOpen_CancelButtonClosesDialogWithoutInvokingContinue()
    {
        var dialogOpen = new State<bool>(true);
        var continued = false;
        var dialog = new UnansweredQuestionsDialog(dialogOpen, 1, () => continued = true);

        var result = dialog.Build();

        Assert.NotNull(result);
        var dialogWidget = Assert.IsType<Dialog>(result);
        var buttons = GetFooterButtons(dialogWidget);

        var cancelButton = buttons.FirstOrDefault(b => b.Title == "Cancel");
        Assert.NotNull(cancelButton);
        Assert.NotNull(cancelButton.OnClick);

        await cancelButton.OnClick.Invoke(new Event<Button>("click", cancelButton));

        Assert.False(dialogOpen.Value);
        Assert.False(continued);
    }
}
