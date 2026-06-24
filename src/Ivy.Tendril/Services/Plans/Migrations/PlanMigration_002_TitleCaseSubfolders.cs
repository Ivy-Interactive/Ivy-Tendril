using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Plans.Migrations;

/// <summary>
///     Renames plan subfolders from lowercase to Title Case for consistency with TENDRIL_HOME folder
///     naming (Inbox, Trash, Plans, ...). Matters on case-sensitive filesystems where readers expect
///     <c>Revisions</c>/<c>Logs</c>/etc. Operates on the folder only; plan.yaml is returned unchanged.
/// </summary>
public sealed class PlanMigration_002_TitleCaseSubfolders : IPlanMigration
{
    private static readonly Dictionary<string, string> TitleCase = new(StringComparer.OrdinalIgnoreCase)
    {
        ["revisions"] = "Revisions",
        ["logs"] = "Logs",
        ["artifacts"] = "Artifacts",
        ["verification"] = "Verification",
        ["worktrees"] = "Worktrees"
    };

    public int Version => 2;
    public string Description => "Title-case plan subfolders (revisions → Revisions, logs → Logs, ...)";

    public string Apply(PlanMigrationContext context)
    {
        foreach (var subDir in Directory.GetDirectories(context.PlanFolder))
        {
            var actualName = Path.GetFileName(subDir);
            if (!TitleCase.TryGetValue(actualName, out var desired)) continue;
            if (actualName == desired) continue;

            try
            {
                // Two-step move via a temp name so the rename is not a no-op on case-insensitive filesystems.
                var tmpPath = subDir + "_tmp";
                Directory.Move(subDir, tmpPath);
                Directory.Move(tmpPath, Path.Combine(context.PlanFolder, desired));
                context.Logger.LogDebug("Renamed {Old} → {New}", actualName, desired);
            }
            catch (Exception ex)
            {
                context.Logger.LogWarning(ex, "Failed to rename {Old} to {New}", subDir, desired);
            }
        }

        return context.Yaml;
    }
}
