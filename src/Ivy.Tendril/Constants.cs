using Ivy.Tendril.Models;

namespace Ivy.Tendril;

/// <summary>
///     Defines the menu ordering for Tendril apps.
///     Lower values appear first in the menu.
/// </summary>
public static class Constants
{
    public const int Dashboard = 10;
    public const int Recommendations = 20;
    public const int Drafts = 30;
    public const int Review = 40;
    public const int Jobs = 50;
    public const int PullRequests = 60;
    public const int Icebox = 70;
    public const int Agent = 80;
    public const int Trash = 90;
    public const int Help = 100;
    public const int Onboarding = 110;

    public const string DocsUrl = "https://tendril.ivy.app";
    public const string DiscordUrl = "https://discord.gg/FHgxkDga3y";
    public const string IssuesUrl = "https://github.com/Ivy-Interactive/Ivy-Tendril/issues/new";
    public const string NewsBaseUrl = "https://cdn.ivy.app/tendril/";

    public static readonly Dictionary<PlanStatus, BadgeVariant> PlanStatusBadgeVariants = new()
    {
        [PlanStatus.Building] = BadgeVariant.Info,
        [PlanStatus.Updating] = BadgeVariant.Info,
        [PlanStatus.Executing] = BadgeVariant.Info,
        [PlanStatus.ReadyForReview] = BadgeVariant.Success,
        [PlanStatus.Failed] = BadgeVariant.Destructive,
        [PlanStatus.Draft] = BadgeVariant.Outline,
        [PlanStatus.Completed] = BadgeVariant.Success,
        [PlanStatus.Skipped] = BadgeVariant.Outline,
        [PlanStatus.Icebox] = BadgeVariant.Outline,
        [PlanStatus.Blocked] = BadgeVariant.Warning
    };

    public static readonly Dictionary<VerificationStatus, BadgeVariant> VerificationStatusBadgeVariants = new()
    {
        [VerificationStatus.Pass] = BadgeVariant.Success,
        [VerificationStatus.Fail] = BadgeVariant.Destructive,
        [VerificationStatus.Pending] = BadgeVariant.Outline,
        [VerificationStatus.Skipped] = BadgeVariant.Outline
    };

    public static readonly Dictionary<JobStatus, Colors> JobStatusColors = new()
    {
        [JobStatus.Running] = Colors.Blue,
        [JobStatus.Completed] = Colors.Green,
        [JobStatus.Failed] = Colors.Red,
        [JobStatus.Timeout] = Colors.Red,
        [JobStatus.Queued] = Colors.Amber,
        [JobStatus.Pending] = Colors.Amber,
        [JobStatus.Stopped] = Colors.Gray,
        [JobStatus.Blocked] = Colors.Orange
    };

    public static readonly Dictionary<string, Colors> JobTypeColors = new()
    {
        [JobTypes.CreatePlan] = Colors.Purple,
        [JobTypes.ExecutePlan] = Colors.Blue,
        [JobTypes.UpdatePlan] = Colors.Cyan,
        [JobTypes.ExpandPlan] = Colors.Teal,
        [JobTypes.SplitPlan] = Colors.Indigo,
        [JobTypes.CreatePr] = Colors.Green,
        [JobTypes.CreateIssue] = Colors.Rose,
        [JobTypes.RetryPlan] = Colors.Orange,
        [JobTypes.UpdateProject] = Colors.Slate,
        [JobTypes.SyncRepo] = Colors.Amber
    };

    /// <summary>
    ///     Job type identifiers for the Tendril promptware execution system.
    /// </summary>
    public static class JobTypes
    {
        public const string CreatePlan = "CreatePlan";
        public const string ExecutePlan = "ExecutePlan";
        public const string RetryPlan = "RetryPlan";
        public const string ExpandPlan = "ExpandPlan";
        public const string UpdatePlan = "UpdatePlan";
        public const string SplitPlan = "SplitPlan";
        public const string CreatePr = "CreatePr";
        public const string CreateIssue = "CreateIssue";
        public const string UpdateProject = "UpdateProject";
        public const string SyncRepo = "SyncRepo";

        public static readonly HashSet<string> BuiltIn = new(StringComparer.OrdinalIgnoreCase)
        {
            CreatePlan, ExecutePlan, RetryPlan, ExpandPlan, UpdatePlan, SplitPlan, CreatePr, CreateIssue, UpdateProject, SyncRepo
        };
    }
}
