using System.Text;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Settings.Dialogs;

/// <summary>
///     Adds or edits one of the environment files recreated inside every plan worktree. Overrides are
///     edited as <c>KEY=VALUE</c> lines - the same shape as the file itself - and support the
///     <c>${ports.&lt;name&gt;}</c>, <c>${env.&lt;VAR&gt;}</c> and <c>%VAR%</c> placeholders described in
///     <see cref="Helpers.EnvironmentMaterializationHelper" />.
/// </summary>
internal class EditProjectEnvFileDialog(
    IState<bool> isOpen,
    int? existingIndex,
    IState<List<ProjectEnvFileConfig>> envFiles) : ViewBase
{
    public override object? Build()
    {
        var editPath = UseState("");
        var editTemplate = UseState("");
        var editOverrides = UseState("");

        UseEffect(() =>
        {
            var files = envFiles.Value;
            if (existingIndex is >= 0 && existingIndex < files.Count)
            {
                var existing = files[existingIndex.Value];
                editPath.Set(existing.Path);
                editTemplate.Set(existing.Template ?? "");
                editOverrides.Set(FormatOverrides(existing.Overrides));
            }
        }, EffectTrigger.OnMount());

        var isNew = existingIndex == null;

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader(isNew ? "Add Environment File" : "Edit Environment File"),
            new DialogBody(
                Layout.Vertical()
                | editPath.ToTextInput("e.g. apps/web/.env").WithField().Label("Path").Required()
                | editTemplate.ToTextInput("e.g. .env.example").WithField().Label("Template")
                | editOverrides.ToCodeInput("PORT=${ports.backend}").Height(Size.Units(40)).WithField().Label("Overrides")
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => isOpen.Set(false)),
                new Button(isNew ? "Add" : "Save").Primary().OnClick(() =>
                {
                    var path = editPath.Value.Trim();
                    if (string.IsNullOrWhiteSpace(path)) return;

                    var entry = new ProjectEnvFileConfig
                    {
                        Path = path,
                        Template = string.IsNullOrWhiteSpace(editTemplate.Value) ? null : editTemplate.Value.Trim(),
                        Overrides = ParseOverrides(editOverrides.Value)
                    };

                    var list = new List<ProjectEnvFileConfig>(envFiles.Value);
                    if (isNew)
                        list.Add(entry);
                    else
                        list[existingIndex!.Value] = entry;

                    envFiles.Set(list);
                    isOpen.Set(false);
                })
            )
        ).Width(Size.Rem(34));
    }

    /// <summary>
    ///     Parses <c>KEY=VALUE</c> lines into overrides. Blank lines and <c>#</c> comments are dropped, and
    ///     a line without <c>=</c> is ignored rather than saved as an empty key, which would produce an
    ///     unparseable env file.
    /// </summary>
    internal static Dictionary<string, string> ParseOverrides(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text)) return result;

        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var separator = line.IndexOf('=');
            if (separator <= 0) continue;

            result[line[..separator].Trim()] = line[(separator + 1)..];
        }

        return result;
    }

    /// <summary>Renders overrides back into the <c>KEY=VALUE</c> lines the editor shows.</summary>
    internal static string FormatOverrides(IReadOnlyDictionary<string, string> overrides)
    {
        var sb = new StringBuilder();
        foreach (var (key, value) in overrides)
            sb.Append(key).Append('=').Append(value).Append('\n');
        return sb.ToString().TrimEnd('\n');
    }
}
