using Ivy;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Apps.Views;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Ivy.Tendril.Apps.Workflows;

public class SidebarView(
    List<WorkflowItem> workflows,
    IState<WorkflowItem?> selectedWorkflow,
    IState<string?> projectFilter,
    IState<string> textFilter,
    IConfigService config,
    Action triggerCreate) : ViewBase
{
    private object BuildHeader()
    {
        var projectOptions = config.Projects
            .Select(p => new Option<string>(p.Name, p.Name))
            .ToArray<IAnyOption>();

        var searchInput = textFilter.ToSearchInput()
            .Placeholder("Search workflows...");

        var header = Layout.Vertical();
        if (projectOptions.Length > 0)
        {
            header |= projectFilter.ToSelectInput(projectOptions)
                .Placeholder("Select project...")
                .WithField().Label("Project");
        }

        header |= (Layout.Vertical().Height(Size.Px(40)).AlignContent(Align.Center) | searchInput)
               | new Button("Create Workflow").Primary().OnClick(triggerCreate);

        return header;
    }

    public override object Build()
    {
        var filteredList = workflows
            .Where(w => string.IsNullOrEmpty(textFilter.Value) || w.Name.Contains(textFilter.Value, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (filteredList.Count == 0 && !string.IsNullOrWhiteSpace(textFilter.Value))
        {
            return new HeaderLayout(BuildHeader(), new NoResultsView());
        }

        var content = new List(filteredList.Select(wf =>
        {
            var clickableWf = wf;
            var badges = Layout.Horizontal()
                | new Badge(clickableWf.IsActive ? "Active" : "Inactive")
                    .Color(clickableWf.IsActive ? Colors.Success : Colors.Muted);

            return SidebarListRow.Build(clickableWf.Name, badges, () => selectedWorkflow.Set(clickableWf));
        }));

        return new HeaderLayout(BuildHeader(), content);
    }
}
