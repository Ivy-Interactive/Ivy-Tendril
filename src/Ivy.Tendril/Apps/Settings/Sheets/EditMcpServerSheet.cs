using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Settings.Sheets;

public class EditMcpServerSheet(
    IState<bool> isOpen,
    int? editingIndex,
    IState<List<ProjectMcpServerRef>> mcpServers) : ViewBase
{
    public override object? Build()
    {
        var editName = UseState("");
        var editCommand = UseState("");
        var editArguments = UseState("");
        var editEnv = UseState("");
        var editDisabled = UseState(false);

        UseEffect(() =>
        {
            var servers = mcpServers.Value;
            if (editingIndex is >= 0 && editingIndex < servers.Count)
            {
                var s = servers[editingIndex.Value];
                editName.Set(s.Name);
                editCommand.Set(s.Command);
                editArguments.Set(string.Join(" ", s.Arguments));
                editEnv.Set(string.Join("\n", s.Environment.Select(kv => $"{kv.Key}={kv.Value}")));
                editDisabled.Set(s.Disabled);
            }
        }, EffectTrigger.OnMount());

        var isNew = editingIndex == null;

        var sheetContent = Layout.Vertical()
            | editName.ToTextInput("Server name (e.g. sqlite)...").WithField().Label("Name").Required()
            | editCommand.ToTextInput("Command executable (e.g. npx)...").WithField().Label("Command").Required()
            | editArguments.ToTextInput("Arguments (e.g. -y @modelcontextprotocol/server-sqlite)...").WithField().Label("Arguments (optional)")
            | editEnv.ToTextareaInput("Environment variables (KEY=VALUE per line)...").Rows(3).WithField().Label("Environment Variables (optional)")
            | editDisabled.ToSwitchInput().WithField().Label("Disabled")
            | (Layout.Horizontal().AlignContent(Align.Right)
               | new Button("Cancel").Outline().OnClick(() => isOpen.Set(false))
               | new Button(isNew ? "Add" : "Save").Primary().OnClick(() =>
               {
                   if (string.IsNullOrWhiteSpace(editName.Value) || string.IsNullOrWhiteSpace(editCommand.Value)) return;

                   var argsList = editArguments.Value
                       .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .ToList();

                   var envDict = new Dictionary<string, string>();
                   foreach (var line in editEnv.Value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                   {
                       var parts = line.Split('=', 2);
                       if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
                           envDict[parts[0].Trim()] = parts[1].Trim();
                   }

                   var updated = new List<ProjectMcpServerRef>(mcpServers.Value);
                   var entry = new ProjectMcpServerRef
                   {
                       Name = editName.Value.Trim(),
                       Command = editCommand.Value.Trim(),
                       Arguments = argsList,
                       Environment = envDict,
                       Disabled = editDisabled.Value
                   };

                   if (editingIndex.HasValue && editingIndex.Value < updated.Count)
                       updated[editingIndex.Value] = entry;
                   else
                       updated.Add(entry);

                   mcpServers.Set(updated);
                   isOpen.Set(false);
               }));

        return new Sheet(
            onClose: () => isOpen.Set(false),
            content: sheetContent,
            title: isNew ? "Add MCP Server" : "Edit MCP Server"
        ).Width(UxHelper.SheetWidth);
    }
}
