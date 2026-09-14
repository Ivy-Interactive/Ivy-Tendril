using System.Globalization;
using Ivy.Tendril.Wireframe.Hosting;

namespace Ivy.Tendril.Services.Wireframes;

/// <summary>
/// Where a plan's wireframes live and where they are served.
///
/// Wireframes are throwaway plan material: they sit in the plan folder beside Revisions/, never
/// in a project repo or a worktree, and exist only to show the user what will be built and to
/// guide the agent that builds it.
/// </summary>
public static class PlanWireframes
{
    public const string FolderName = "Wireframes";

    /// <summary>The address a plan's <c>wireframe</c> fences resolve their names against.</summary>
    public static string BaseUrl(int planId) =>
        $"{WireframeHost.RoutePrefix}/{planId.ToString(CultureInfo.InvariantCulture)}/";

    /// <summary>
    /// Why wireframes may not be made at <paramref name="path" />, or null when they may. They may not
    /// inside a plan whose project has <see cref="ProjectConfig.Wireframes" /> turned off. Outside any
    /// plan, or without configuration to read, the answer is always yes.
    /// </summary>
    public static string? DisabledReason(string path)
    {
        try
        {
            var planFolder = FindPlanFolder(Path.GetFullPath(path));
            if (planFolder is null) return null;

            var planYaml = Helpers.PlanYamlHelper.ReadPlanYaml(planFolder);
            if (string.IsNullOrEmpty(planYaml?.Project)) return null;

            var project = new ConfigService().GetProject(planYaml.Project);
            return project is { Wireframes: false }
                ? $"Project '{project.Name}' has wireframes turned off (wireframes: false in its configuration), " +
                  "so its plans do not get wireframes. Describe the change in the plan instead."
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The nearest enclosing plan folder: a <c>NNNNN-Title</c> directory holding a plan.yaml.</summary>
    private static string? FindPlanFolder(string path)
    {
        for (var dir = new DirectoryInfo(path); dir is not null; dir = dir.Parent)
        {
            if (dir.Name.Length > 6 && dir.Name[..5].All(char.IsAsciiDigit) && dir.Name[5] == '-' &&
                File.Exists(Path.Combine(dir.FullName, "plan.yaml")))
                return dir.FullName;
        }
        return null;
    }

    /// <summary>
    /// The project directory for one of a plan's wireframes, or null when the plan does not exist.
    /// The directory itself may not exist yet; the host reports that as a missing wireframe.
    /// </summary>
    public static string? ResolveRoot(string? plansDirectory, string scope, string name)
    {
        if (string.IsNullOrEmpty(plansDirectory) || !Directory.Exists(plansDirectory)) return null;
        if (!WireframeHost.IsValidName(name)) return null;
        if (!int.TryParse(scope, NumberStyles.None, CultureInfo.InvariantCulture, out var planId)) return null;

        var folder = Directory.GetDirectories(plansDirectory, $"{planId:D5}-*").FirstOrDefault();
        return folder is null ? null : Path.Combine(folder, FolderName, name);
    }
}
