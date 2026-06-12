namespace Ivy.Tendril.Helpers;

public static class UxHelper
{
    public static Responsive<Size> SheetWidth =>
        Size.Full().At(Breakpoint.Mobile).And(Breakpoint.Desktop, Size.Half());


    public static async Task AnimateProgressAsync(IState<int?> value, CancellationToken ct, double duration = 15.0, double ceiling = 92.0)
    {
        const int steps = 100;
        var interval = TimeSpan.FromMilliseconds(duration * 1000.0 / steps);

        value.Set(0);
        for (var tick = 1; tick <= steps; tick++)
        {
            try { await Task.Delay(interval, ct); }
            catch (OperationCanceledException) { return; }

            var t = tick / (double)steps;
            var progress = (int)(DualLinearEasing(t) * ceiling);
            value.Set(progress);
        }
    }

    private static double DualLinearEasing(double t)
    {
        const double transitionPoint = 0.25;
        if (t <= transitionPoint)
            return (t / transitionPoint) * 0.75;

        var phaseProgress = (t - transitionPoint) / (1.0 - transitionPoint);
        return 0.75 + 0.25 * phaseProgress;
    }
}
