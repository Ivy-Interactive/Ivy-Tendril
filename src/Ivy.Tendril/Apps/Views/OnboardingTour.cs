using Ivy.Core.Apps;
using Ivy.Tendril.Apps.Drafts;
using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Apps.Review;
using Ivy.Tendril.Services;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Views;

/// <summary>
/// Post-onboarding walkthrough shown once after setup completes. Renders a single
/// floating <see cref="OnboardingTourPopover"/> that points at the sidebar element
/// for the current step, so it never affects the layout of the views it explains.
/// </summary>
public static class OnboardingTour
{
    public const string NewPlanButtonTestId = "new-plan-button";

    private sealed record TourStep(string Title, string Description, Func<IAppRepository, string?> AnchorSelector);

    private static readonly TourStep[] Steps =
    [
        new("Create your first plan",
            "Describe a change you want made and Tendril turns it into a plan an agent can execute.",
            _ => $"[data-testid=\"{NewPlanButtonTestId}\"]"),
        new("Refine it in Drafts",
            "New plans land in Drafts. Review the proposed steps, make adjustments, and start execution when it looks right.",
            repo => MenuItemSelector(repo, typeof(DraftsApp))),
        new("Follow along in Jobs",
            "Jobs shows everything Tendril is working on in the background — planning, executing and creating PRs.",
            repo => MenuItemSelector(repo, typeof(JobsApp))),
        new("Accept work in Review",
            "Completed work lands in Review. Inspect the result and accept it, or send it back with change requests.",
            repo => MenuItemSelector(repo, typeof(ReviewApp)))
    ];

    private static string? MenuItemSelector(IAppRepository repo, Type appType) =>
        repo.GetApp(appType)?.Id is { } id ? $"[data-menu-item=\"{id}\"]" : null;

    public static object? Build(IOnboardingTourService tourService, IAppRepository appRepository)
    {
        if (tourService.Step is not { } step || step < 0 || step >= Steps.Length)
            return null;

        var def = Steps[step];
        if (def.AnchorSelector(appRepository) is not { } anchor)
            return null;

        return new OnboardingTourPopover(anchor)
            .Title(def.Title)
            .Description(def.Description)
            .Step(step, Steps.Length)
            .Placement("right")
            .OnNext(() =>
            {
                if (step >= Steps.Length - 1) tourService.Dismiss();
                else tourService.SetStep(step + 1);
            })
            .OnBack(() =>
            {
                if (step > 0) tourService.SetStep(step - 1);
            })
            .OnDismiss(tourService.Dismiss);
    }
}
