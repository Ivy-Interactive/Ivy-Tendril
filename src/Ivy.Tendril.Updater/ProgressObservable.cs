using System.Reactive.Linq;

namespace Ivy.Tendril.Updater;

public static class ProgressObservable
{
    public delegate double EasingFunction(double t);

    private static double LinearEasing(double t) => t;

    public static IObservable<int> Create(double duration = 10, EasingFunction? easingFunction = null)
    {
        var easing = easingFunction ?? LinearEasing;
        double updateInterval = (duration * 1000.0) / 100;

        return Observable.Interval(TimeSpan.FromMilliseconds(updateInterval))
            .Take(101)
            .Select(tick =>
            {
                double t = tick / 100.0;
                double progress = easing(t) * 100.0;
                return (int)progress;
            });
    }
}

public static class EasingFunctions
{
    public static double DualLinearEasing(double t)
    {
        const double transitionPoint = 0.25;
        if (t <= transitionPoint)
        {
            return (t / transitionPoint) * 0.75;
        }
        else
        {
            double startVal = 0.75;
            double endVal = 1.0;
            double phaseDuration = 1.0 - transitionPoint;
            double phaseProgress = (t - transitionPoint) / phaseDuration;
            return startVal + (endVal - startVal) * phaseProgress;
        }
    }
}
