using System.Text.RegularExpressions;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Onboarding;

public class TendrilHomeStepView(
    IState<int> stepperIndex,
    IState<string> tendrilHomePath,
    IState<bool> homeBootstrapped) : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var setupService = UseService<IOnboardingSetupService>();

        var error = UseState<string?>(null);
        var isBootstrapping = UseState(false);

        var defaultHome = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".tendril");

        return Layout.Vertical().Gap(4).Margin(0, 0, 0, 20)
               | Text.H2("This is where we store your data")
               | Text.Muted(
                   "Tendril keeps your config, plans, inbox, trash, hooks, promptwares, and a local " +
                   "SQLite database in this folder. The default works for most people. Change it if " +
                   "you want this data on a different drive, synced via Dropbox/iCloud, or shared " +
                   "between machines.")
               | (error.Value != null ? Text.Danger(error.Value) : null!)
               | tendrilHomePath.ToTextInput("Path to data folder...")
                   .WithField().Label("Tendril Home")
               | Text.Muted($"Default: {defaultHome}")
               | (Layout.Horizontal().Width(Size.Full())
                  | new Button("Back").Outline().Large().Icon(Icons.ArrowLeft)
                      .Disabled(isBootstrapping.Value)
                      .OnClick(() => stepperIndex.Set(stepperIndex.Value - 1))
                  | new Spacer()
                  | new Button("Next").Primary().Large().Icon(Icons.ArrowRight, Align.Right)
                      .Disabled(isBootstrapping.Value || string.IsNullOrWhiteSpace(tendrilHomePath.Value))
                      .Loading(isBootstrapping.Value)
                      .OnClick(async () =>
                      {
                          if (string.IsNullOrWhiteSpace(tendrilHomePath.Value))
                          {
                              error.Set("Please provide a valid path.");
                              return;
                          }

                          string resolved;
                          try
                          {
                              resolved = ResolvePath(tendrilHomePath.Value);
                          }
                          catch (Exception ex)
                          {
                              error.Set($"Invalid path: {ex.Message}");
                              return;
                          }

                          error.Set(null);
                          isBootstrapping.Set(true);
                          try
                          {
                              config.SetPendingTendrilHome(resolved);
                              await setupService.BootstrapTendrilHomeAsync(resolved);
                              homeBootstrapped.Set(true);
                              tendrilHomePath.Set(resolved);
                              stepperIndex.Set(stepperIndex.Value + 1);
                          }
                          catch (Exception ex)
                          {
                              error.Set($"Failed to set up data folder: {ex.Message}");
                          }
                          finally
                          {
                              isBootstrapping.Set(false);
                          }
                      }));
    }

    private static string ResolvePath(string raw)
    {
        var path = VariableExpansion.ExpandVariables(raw, "");

        if (path.StartsWith("~"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (path == "~") path = home;
            else if (path.StartsWith("~/") || path.StartsWith("~\\"))
                path = Path.Combine(home, path.Substring(2));
        }
        else if (path.StartsWith("$"))
        {
            var match = Regex.Match(path, @"^\$([A-Za-z_][A-Za-z0-9_]*)");
            if (match.Success)
            {
                var varName = match.Groups[1].Value;
                var varValue = Environment.GetEnvironmentVariable(varName);
                if (!string.IsNullOrEmpty(varValue))
                    path = varValue + path.Substring(match.Length);
            }
        }

        return Path.GetFullPath(path);
    }
}
