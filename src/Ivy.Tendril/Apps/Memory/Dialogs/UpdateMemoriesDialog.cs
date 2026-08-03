using System;
using System.Collections.Generic;
using System.Linq;
using Ivy;

namespace Ivy.Tendril.Apps.Memory.Dialogs;

public class UpdateMemoriesDialog(
    IState<bool> isOpen,
    List<string> projectFiles,
    IState<bool> isFilesLoading,
    Action onLoadProjectFiles,
    Action<List<string>> onStartJob,
    IClientProvider client) : ViewBase
{
    public override object? Build()
    {
        var selectedFilesState = UseState(new HashSet<string>());

        if (!isOpen.Value) return null;

        object fileSelector;
        if (isFilesLoading.Value)
        {
            fileSelector = Layout.Center()
                           | Icons.LoaderCircle.ToIcon().Color(Colors.Primary).WithAnimation(AnimationType.Rotate)
                           | Text.Muted("Scanning project files...");
        }
        else if (projectFiles.Count == 0)
        {
            fileSelector = Layout.Vertical().Gap(2)
                           | Text.Muted("No project files detected or project not set.")
                           | new Button("Refresh Files").Outline().OnClick(onLoadProjectFiles);
        }
        else
        {
            var buttons = projectFiles.Select(file =>
            {
                var isChecked = selectedFilesState.Value.Contains(file);
                var btn = isChecked ? new Button(file).Primary() : new Button(file).Outline();
                return btn.OnClick(() =>
                {
                    var set = new HashSet<string>(selectedFilesState.Value);
                    if (isChecked) set.Remove(file);
                    else set.Add(file);
                    selectedFilesState.Set(set);
                }) as object;
            }).ToArray();

            fileSelector = Layout.Vertical().Scroll(Scroll.Auto).Height(Size.Px(250))
                           | new Fragment(buttons);
        }

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader("Agentic Memory Update"),
            new DialogBody(
                Layout.Vertical().Gap(3)
                | Text.Muted("Select codebase files to document or update in memory vault via background agent.")
                | fileSelector
            ),
            new DialogFooter(
                Layout.Horizontal().Gap(2).Right()
                | new Button("Cancel").Outline().OnClick(() => isOpen.Set(false))
                | new Button("Start Job").Primary().Icon(Icons.Sparkles).OnClick(() =>
                  {
                      var list = selectedFilesState.Value.ToList();
                      if (list.Count == 0)
                      {
                          client.Toast("Please select at least one file.", "Selection Required");
                          return;
                      }

                      onStartJob(list);
                      isOpen.Set(false);
                      client.Toast($"Started UpdateMemories job for {list.Count} file(s)", "Job Queued");
                  })
            )
        ).Width(Size.Rem(40));
    }
}
