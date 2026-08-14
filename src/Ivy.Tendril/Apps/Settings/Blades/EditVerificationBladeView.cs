using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Settings.Blades;

public class EditVerificationBladeView(
    IConfigService config,
    IClientProvider client,
    RefreshToken refreshToken,
    IState<List<ProjectVerificationRef>> projectVerifications) : ViewBase
{
    public override object? Build()
    {
        var bladeContext = UseContext<IBladeContext>();
        var editName = UseState("");
        var editPrompt = UseState("");

        return Layout.Vertical()
            | editName.ToTextInput("Verification name...").WithField().Label("Name")
            | editPrompt.ToCodeInput("Verification prompt...").Language(Languages.Markdown).Height(Size.Units(60)).WithField().Label("Prompt")
            | Layout.Horizontal()
                | new Button("Cancel").Outline().OnClick(() => bladeContext.Pop(this))
                | new Button("Add").Primary().OnClick(() =>
                {
                    if (string.IsNullOrWhiteSpace(editName.Value)) return;

                    var verifications = config.Settings.Verifications;
                    verifications.Add(new VerificationConfig
                    {
                        Name = editName.Value,
                        Prompt = editPrompt.Value
                    });

                    try
                    {
                        config.SaveSettings();

                        var list = new List<ProjectVerificationRef>(projectVerifications.Value);
                        list.Add(new ProjectVerificationRef { Name = editName.Value, Required = false });
                        projectVerifications.Set(list);

                        refreshToken.Refresh();
                        client.Toast("Verification added", "Saved");
                        bladeContext.Pop(this);
                    }
                    catch (Exception ex)
                    {
                        verifications.RemoveAt(verifications.Count - 1);
                        refreshToken.Refresh();
                        client.Toast($"Failed to add verification: {ex.Message}", "Error");
                    }
                });
    }
}
