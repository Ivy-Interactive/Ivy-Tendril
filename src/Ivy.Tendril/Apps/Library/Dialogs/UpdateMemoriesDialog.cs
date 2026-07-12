using System;
using System.Collections.Generic;
using System.Linq;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Library.Dialogs;

public class FileSelectionRowView(
    string filePath,
    IState<HashSet<string>> selectedFiles) : ViewBase
{
    public override object Build()
    {
        var isChecked = UseState(() => selectedFiles.Value.Contains(filePath));
        var previous = UseState(isChecked.Value);

        // Keep local checkbox state in sync if selectedFiles is updated externally (like Select All)
        var isCurrentlySelected = selectedFiles.Value.Contains(filePath);
        if (isChecked.Value != isCurrentlySelected)
        {
            isChecked.Set(isCurrentlySelected);
            previous.Set(isCurrentlySelected);
        }

        UseEffect(() =>
        {
            if (isChecked.Value == previous.Value) return;
            previous.Set(isChecked.Value);
            var set = new HashSet<string>(selectedFiles.Value);
            if (isChecked.Value)
                set.Add(filePath);
            else
                set.Remove(filePath);
            selectedFiles.Set(set);
        }, isChecked);

        return isChecked.ToBoolInput(filePath);
    }
}

public class UpdateMemoriesDialog(
    IState<bool> isOpen,
    IState<List<string>> projectFiles,
    IState<bool> isFilesLoading,
    Action onLoadFiles,
    Action<List<string>> onStartUpdate,
    IClientProvider client) : ViewBase
{
    public override object? Build()
    {
        var searchQuery = UseState("");
        var selectedFiles = UseState(new HashSet<string>());

        if (!isOpen.Value) return null;

        var files = projectFiles.Value;
        var filteredList = files;
        if (!string.IsNullOrWhiteSpace(searchQuery.Value))
        {
            var q = searchQuery.Value.ToLowerInvariant();
            filteredList = files.Where(f => f.ToLowerInvariant().Contains(q)).ToList();
        }

        object content;
        if (isFilesLoading.Value)
        {
            content = Layout.Center()
                      | Icons.LoaderCircle.ToIcon().Color(Colors.Primary).WithAnimation(AnimationType.Rotate)
                      | Text.Muted("Loading project files...");
        }
        else if (files.Count == 0)
        {
            content = Layout.Vertical().AlignContent(Align.Center).Gap(2)
                      | Text.Muted("No files found in the active project.")
                      | new Button("Scan Files").Icon(Icons.RefreshCw).OnClick(onLoadFiles);
        }
        else
        {
            var selectionControls = Layout.Horizontal().Gap(3)
                | new Button("Select All").Link().OnClick(() =>
                {
                    var updated = new HashSet<string>(selectedFiles.Value);
                    foreach (var f in filteredList)
                    {
                        updated.Add(f);
                    }
                    selectedFiles.Set(updated);
                })
                | new Button("Deselect All").Link().OnClick(() =>
                {
                    var updated = new HashSet<string>(selectedFiles.Value);
                    foreach (var f in filteredList)
                    {
                        updated.Remove(f);
                    }
                    selectedFiles.Set(updated);
                });

            var fileList = new List(filteredList.Select(f =>
                new FileSelectionRowView(f, selectedFiles) as object
            ));

            content = Layout.Vertical().Gap(2)
                | searchQuery.ToSearchInput().Placeholder("Search files...")
                | selectionControls
                | Layout.Vertical().Scroll(Scroll.Auto).Height(Size.Px(300)) | fileList;
        }

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader("Agentic Memory Updater"),
            new DialogBody(content),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => isOpen.Set(false)),
                new Button($"Update ({selectedFiles.Value.Count} files)")
                    .Primary()
                    .Icon(Icons.Sparkles)
                    .Disabled(selectedFiles.Value.Count == 0 || isFilesLoading.Value)
                    .OnClick(() =>
                    {
                        onStartUpdate(selectedFiles.Value.ToList());
                        isOpen.Set(false);
                    })
            )
        );
    }
}
