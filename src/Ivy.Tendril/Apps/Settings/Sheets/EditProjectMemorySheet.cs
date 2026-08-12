using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps.Settings.Sheets;

public class EditProjectMemorySheet(
    IState<bool> isOpen,
    string tendrilHome,
    string projectName,
    string? existingFileName,
    IState<int> refreshCounter) : ViewBase
{
    public override object? Build()
    {
        var editFileName = UseState(existingFileName ?? "stack.md");
        var editContent = UseState("");

        UseEffect(() =>
        {
            if (!string.IsNullOrWhiteSpace(existingFileName))
            {
                var memoryDir = ProjectPathHelper.GetMemoryDir(tendrilHome, projectName);
                var fullPath = Path.Combine(memoryDir, existingFileName);
                if (File.Exists(fullPath))
                {
                    editFileName.Set(existingFileName);
                    editContent.Set(File.ReadAllText(fullPath));
                }
            }
        }, EffectTrigger.OnMount());

        var isNew = string.IsNullOrWhiteSpace(existingFileName);

        var sheetContent = Layout.Vertical()
            | editFileName.ToTextInput("Memory filename (e.g. stack.md, conventions.md)...").WithField().Label("Filename").Required()
            | editContent.ToTextareaInput("Markdown memory content (e.g. tech stack, rules, architectural conventions)...").Rows(8).WithField().Label("Content (Markdown)").Required()
            | (Layout.Horizontal().AlignContent(Align.Right)
               | new Button("Cancel").Outline().OnClick(() => isOpen.Set(false))
               | new Button(isNew ? "Add" : "Save").Primary().OnClick(() =>
               {
                   if (string.IsNullOrWhiteSpace(editFileName.Value)) return;

                   var memoryDir = ProjectPathHelper.GetMemoryDir(tendrilHome, projectName);
                   Directory.CreateDirectory(memoryDir);

                   var fileName = editFileName.Value.Trim();
                   if (!fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                       fileName += ".md";

                   var fullPath = Path.Combine(memoryDir, fileName);
                   File.WriteAllText(fullPath, editContent.Value);

                   refreshCounter.Set(refreshCounter.Value + 1);
                   isOpen.Set(false);
               }));

        return new Sheet(
            onClose: () => isOpen.Set(false),
            content: sheetContent,
            title: isNew ? "Add Project Memory" : $"Edit Memory: {existingFileName}"
        ).Width(UxHelper.SheetWidth);
    }
}
