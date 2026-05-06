using Ivy.Core.Hooks;
using Ivy.Tendril.Helpers;
using Ivy.Desktop;

namespace Ivy.Tendril.Views;

public class GitHubRepoSelectorView(IState<string> repoUrl, IState<string> baseBranch, Action? onRemove = null) : ViewBase
{
    public override object Build()
    {
        var usePicker = UseState(false);
        var loading = UseState(false);
        
        var owners = UseState<string[]>([]);
        var repos = UseState<string[]>([]);
        var branches = UseState<string[]>([]);
        
        var selectedOwner = UseState<string>("");
        var selectedRepo = UseState<string>("");
        var selectedBranch = UseState<string>("");

        UseEffect(async () =>
        {
            if (usePicker.Value && owners.Value.Length == 0)
            {
                loading.Set(true);
                var res = await GitHubCliHelper.GetOwnersAsync();
                owners.Set(res);
                if (res.Length > 0) selectedOwner.Set(res[0]);
                loading.Set(false);
            }
        }, usePicker);

        UseEffect(async () =>
        {
            selectedRepo.Set("");
            repos.Set([]);
            if (!string.IsNullOrEmpty(selectedOwner.Value))
            {
                loading.Set(true);
                var res = await GitHubCliHelper.GetRepositoriesAsync(selectedOwner.Value);
                repos.Set(res);
                if (res.Length > 0) selectedRepo.Set(res[0]);
                else selectedRepo.Set("");
                loading.Set(false);
            }
        }, selectedOwner);

        UseEffect(async () =>
        {
            selectedBranch.Set("");
            branches.Set([]);
            if (!string.IsNullOrEmpty(selectedOwner.Value) && !string.IsNullOrEmpty(selectedRepo.Value))
            {
                loading.Set(true);
                var res = await GitHubCliHelper.GetBranchesAsync(selectedOwner.Value, selectedRepo.Value);
                branches.Set(res);
                if (res.Length > 0) selectedBranch.Set(res[0]);
                else selectedBranch.Set("");
                loading.Set(false);
            }
        }, selectedRepo);

        UseEffect(() =>
        {
            if (usePicker.Value && !string.IsNullOrEmpty(selectedOwner.Value) && !string.IsNullOrEmpty(selectedRepo.Value))
            {
                var url = $"https://github.com/{selectedOwner.Value}/{selectedRepo.Value}.git";
                repoUrl.Set(url);
                baseBranch.Set(selectedBranch.Value);
            }
        }, selectedOwner, selectedRepo, selectedBranch);

        var rightControls = Layout.Horizontal().Gap(2).AlignContent(Align.Right)
                     | new Icon(Icons.Github)
                     | usePicker.ToSwitchInput(label: "Browser")
                     | (onRemove != null ? (object)new Button().Icon(Icons.Trash).Ghost().OnClick(onRemove).WithTooltip("Remove repository") : null!);

        if (usePicker.Value)
        {
            return Layout.Vertical().Gap(2).Width(Size.Grow())
                   | (Layout.Horizontal().Gap(2).Width(Size.Grow())
                      | selectedOwner.ToSelectInput(owners.Value, disabled: loading.Value).Width(Size.Grow())
                      | selectedRepo.ToSelectInput(repos.Value, disabled: loading.Value).Width(Size.Grow())
                      | selectedBranch.ToSelectInput(branches.Value, disabled: loading.Value).Width(Size.Grow())
                      | rightControls)
                   | (loading.Value ? Text.Muted("Loading GitHub data...") : null!);
        }

        return Layout.Horizontal().Gap(2).Width(Size.Grow())
               | repoUrl.ToTextInput("Repository URL (e.g. https://github.com/owner/repo.git)")
                   .Width(Size.Grow())
               | rightControls;
    }
}
