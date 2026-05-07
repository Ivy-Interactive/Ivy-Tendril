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
    public const int Claude = 80;
    public const int Trash = 90;
    public const int Help = 100;
    public const int Onboarding = 110;

    public const string DocsUrl = "https://tendril.ivy.app";
    public const string DiscordUrl = "https://discord.gg/FHgxkDga3y";
    public const string IssuesUrl = "https://github.com/Ivy-Interactive/Ivy-Tendril/issues/new";
    public const string NewsBaseUrl = "https://cdn.ivy.app/tendril/";

    /// <summary>
    ///     Job type identifiers for the Tendril promptware execution system.
    /// </summary>
    public static class JobTypes
    {
        public const string CreatePlan = "CreatePlan";
        public const string ExecutePlan = "ExecutePlan";
        public const string ExpandPlan = "ExpandPlan";
        public const string UpdatePlan = "UpdatePlan";
        public const string SplitPlan = "SplitPlan";
        public const string CreatePr = "CreatePr";
        public const string CreateIssue = "CreateIssue";
        public const string UpdateProject = "UpdateProject";

        public static readonly HashSet<string> BuiltIn = new(StringComparer.OrdinalIgnoreCase)
        {
            CreatePlan, ExecutePlan, ExpandPlan, UpdatePlan, SplitPlan, CreatePr, CreateIssue, UpdateProject
        };
    }
}