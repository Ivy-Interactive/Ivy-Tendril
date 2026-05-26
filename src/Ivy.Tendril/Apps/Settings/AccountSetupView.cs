using Ivy.Desktop;

namespace Ivy.Tendril.Apps.Settings;

public class AccountSetupView : ViewBase
{
    public override object Build()
    {
        // Hooks first, then Context.TryUseService, then UseEffect — matches analyzer rules
        var client = UseService<IClientProvider>();
        var user = UseState<UserInfo?>();
        Context.TryUseService<IAuthService>(out var auth);

        UseEffect(async () =>
        {
            if (auth != null)
                user.Set(await auth.GetUserInfoAsync());
        });

        async Task OnLogout()
        {
            if (auth == null) return;
            try { await auth.LogoutAsync(); }
            catch { /* ignore */ }
        }

        var form = Layout.Vertical().Padding(4).Width(Size.Auto().Max(Size.Units(120)))
                   | Text.Block("Account").Bold()
                   | Text.Block("Your account details.").Muted().Small();

        if (user.Value != null)
        {
            form |= Layout.Horizontal().Gap(3).AlignContent(Align.Center)
                    | new Avatar(user.Value.Initials, user.Value.AvatarUrl)
                    | (Layout.Vertical().Gap(1)
                       | (user.Value.FullName != null ? (object)Text.Block(user.Value.FullName!) : null!)
                       | Text.Muted(user.Value.Email));

            form |= new Separator();

            form |= new Button("Logout")
                .Secondary()
                .Icon(Icons.LogOut)
                .OnClick(async () => await OnLogout());
        }
        else if (auth == null)
        {
            form |= Text.Muted("Authentication is not configured.");
        }
        else
        {
            form |= Text.Muted("Loading…");
        }

        return form;
    }
}
