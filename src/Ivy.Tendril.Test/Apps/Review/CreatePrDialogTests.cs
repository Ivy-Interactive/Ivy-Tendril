using Ivy;
using Ivy.Core.Hooks;
using Ivy.Tendril.Apps.Review.Dialogs;

namespace Ivy.Tendril.Test.Apps.Review;

public class CreatePrDialogTests
{
    [Fact]
    public void BuildTargetBranchField_RendersSearchableSelect_WithProjectConfiguredBranchSelected()
    {
        var selectedBranch = new State<string>("development");
        var isCustomBranch = new State<bool>(false);
        var customBranchText = new State<string>("");
        var branches = new[] { "development", "feature/foo", "main" };

        var fieldObj = CreatePrDialog.BuildTargetBranchField(
            selectedBranch,
            isCustomBranch,
            customBranchText,
            branches,
            "development");

        Assert.NotNull(fieldObj);
        var field = Assert.IsType<Field>(fieldObj);
        Assert.Equal("Target Branch", field.Label);

        var select = Assert.IsType<SelectInput<string>>(Assert.Single(field.Children));
        Assert.True(select.Searchable);
        Assert.Equal("development", select.Value);
    }

    [Fact]
    public void BuildTargetBranchField_CustomBranchToggle_RendersTextInput()
    {
        var selectedBranch = new State<string>("development");
        var isCustomBranch = new State<bool>(true);
        var customBranchText = new State<string>("custom-epic-branch");
        var branches = new[] { "development", "feature/foo", "main" };

        var layoutObj = CreatePrDialog.BuildTargetBranchField(
            selectedBranch,
            isCustomBranch,
            customBranchText,
            branches,
            "development");

        Assert.NotNull(layoutObj);
        var layout = Assert.IsType<LayoutView>(layoutObj);

        var elementsField = typeof(LayoutView).GetField("_elements", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var field = ((System.Collections.IEnumerable)elementsField?.GetValue(layout)!)
            .Cast<object>()
            .Select(el => el.GetType().GetProperty("Content")?.GetValue(el))
            .OfType<Field>()
            .FirstOrDefault();

        Assert.NotNull(field);
        var textInput = Assert.IsType<TextInput<string>>(Assert.Single(field.Children));
        Assert.Equal("custom-epic-branch", textInput.Value);
    }
}
