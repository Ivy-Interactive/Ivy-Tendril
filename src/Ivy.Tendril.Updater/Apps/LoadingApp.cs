using System.Diagnostics;
using Ivy;

namespace Ivy.Tendril.Updater.Apps;

[App()]
public class LoadingApp : ViewBase
{
    public override object? Build()
    {
        var loading = UseState(true);
        var failed = UseState(false);

        UseEffect(async () =>
        {
            var updateResult = await Update();
            if (!updateResult)
            {
                failed.Set(true);
                loading.Set(false);
                return;
            }

            var unpackResult = await Unpack();
            if (!unpackResult)
            {
                failed.Set(true);
                loading.Set(false);
                return;
            }

            loading.Set(false);
        }, [EffectTrigger.OnMount()]);

        if (failed.Value)
            return Layout.Center() | new FailedView();
        return Layout.Center() | (loading.Value ? new LoadingView() : new FinishedView());
    }

    private async Task<bool> Update()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "tool update -g Ivy.Tendril",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process();
        process.StartInfo = startInfo;
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }

    private async Task<bool> Unpack()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "tendril",
            Arguments = "version",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process();
        process.StartInfo = startInfo;
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }
}

public class LoadingView : ViewBase
{
    public override object? Build()
    {
        var progress = UseState(0);

        this.UseEffect(() =>
        {
            var progressObservable = ProgressObservable.Create(70, EasingFunctions.DualLinearEasing);
            return progressObservable.Subscribe(
                onNext: p => progress.Set(p)
            );
        });

        void OnLinkClick(Event<Markdown, string> @event)
        {
            ProcessHelper.OpenBrowser(@event.Value);
        }

        return new Animation(AnimationType.FadeIn).Repeat(0)
               | (Layout.Vertical().Width(100)
                  | new Loading()
                  | Text.H2("Updating Ivy.Tendril...")
                  | new Progress(progress)
                  | new Markdown(
                      "While you wait, please star us on [GitHub](https://github.com/Ivy-Interactive/Ivy-Tendril).",
                      OnLinkClick)
               );
    }
}

public class FinishedView : ViewBase
{
    public override object? Build()
    {
        return new Animation(AnimationType.FadeIn).Repeat(0)
               | (Layout.Vertical().Width(100)
                  | new Confetti(Text.H2("Ivy.Tendril has been updated")).Trigger(AnimationTrigger.Auto)
                  | Text.Muted("You can now close this window."));
    }
}

public class FailedView : ViewBase
{
    public override object? Build()
    {
        return new Animation(AnimationType.FadeIn).Repeat(0)
               | (Layout.Vertical().Width(100)
                  | Text.H2("Update Failed")
                  | Text.Markdown("Please rerun the installation script to repair your installation.")
                  | Text.Muted("You can now close this window."));
    }
}
