using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Settings.Blades;

public class EditMcpServerBladeView(
    int? existingIndex,
    IState<List<ProjectMcpServerRef>> mcpServers) : ViewBase
{
    public override object? Build()
    {
        var bladeContext = UseContext<IBladeContext>();
        var editName = UseState("");
        var editCommand = UseState("");
        var editArguments = UseState("");
        var editEnv = UseState("");

        UseEffect(() =>
        {
            var servers = mcpServers.Value;
            if (existingIndex is >= 0 && existingIndex < servers.Count)
            {
                var s = servers[existingIndex.Value];
                editName.Set(s.Name);
                editCommand.Set(s.Command);
                editArguments.Set(string.Join(" ", s.Arguments));
                editEnv.Set(string.Join("\n", s.Environment.Select(kv => $"{kv.Key}={kv.Value}")));
            }
        }, EffectTrigger.OnMount());

        var isNew = existingIndex == null;

        return Layout.Vertical()
            | editName.ToTextInput("Server name (e.g. sqlite)...").WithField().Label("Name").Required()
            | editCommand.ToTextInput("Command executable (e.g. npx)...").WithField().Label("Command").Required()
            | editArguments.ToTextInput("Arguments (e.g. -y @modelcontextprotocol/server-sqlite)...").WithField().Label("Arguments")
            | editEnv.ToTextareaInput("Environment variables (KEY=VALUE per line)...").Rows(3).WithField().Label("Environment Variables")
            | Layout.Horizontal()
                | new Button("Cancel").Outline().OnClick(() => bladeContext.Pop(this))
                | new Button(isNew ? "Add" : "Save").Primary().OnClick(() =>
                {
                    if (string.IsNullOrWhiteSpace(editName.Value)) return;
                    if (string.IsNullOrWhiteSpace(editCommand.Value)) return;

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

                    var list = new List<ProjectMcpServerRef>(mcpServers.Value);
                    var entry = new ProjectMcpServerRef
                    {
                        Name = editName.Value.Trim(),
                        Command = editCommand.Value.Trim(),
                        Arguments = argsList,
                        Environment = envDict,
                        Disabled = false
                    };

                    if (isNew)
                        list.Add(entry);
                    else
                        list[existingIndex!.Value] = entry;

                    mcpServers.Set(list);
                    bladeContext.Pop(this);
                });
    }
}
