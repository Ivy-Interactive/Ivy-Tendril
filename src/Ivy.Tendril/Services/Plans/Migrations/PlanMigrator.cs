using System.Reflection;
using System.Text.RegularExpressions;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Services.Plans.Migrations;

/// <summary>
///     Discovers and runs the <see cref="IPlanMigration" /> set against on-disk plans. The per-plan
///     parallel to <see cref="Database.DatabaseMigrator" />: migrations are auto-discovered via
///     reflection, validated to be sequential, and each plan is brought up to <see cref="LatestVersion" />
///     based on the <c>schemaVersion</c> read from its raw plan.yaml.
///
///     Terminal plans (Completed/Skipped) are immutable history and are skipped entirely — they are
///     never migrated or stamped.
/// </summary>
public class PlanMigrator
{
    private static readonly Regex StateLineRegex = new(@"(?m)^state:\s*(.+)$", RegexOptions.Compiled);

    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
        { nameof(PlanStatus.Completed), nameof(PlanStatus.Skipped) };

    private readonly ILogger _logger;
    private readonly List<IPlanMigration> _migrations;

    public PlanMigrator(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger<PlanMigrator>.Instance;
        _migrations = LoadMigrations();
    }

    internal PlanMigrator(IEnumerable<IPlanMigration> migrations, ILogger? logger = null)
    {
        _logger = logger ?? NullLogger<PlanMigrator>.Instance;
        _migrations = migrations.OrderBy(m => m.Version).ToList();
        ValidateMigrationSequence(_migrations);
    }

    /// <summary>Highest migration version; the version every up-to-date plan is stamped with.</summary>
    public int LatestVersion => _migrations.Count > 0 ? _migrations.Max(m => m.Version) : 0;

    /// <summary>
    ///     Migrates every plan folder under <paramref name="plansDirectory" />. <paramref name="onPlanChanged" />
    ///     is invoked with the folder name after a plan is written (used to notify watchers / invalidate caches).
    ///     Best-effort: a failure on one plan is logged and does not stop the others.
    /// </summary>
    public void MigratePlans(string plansDirectory, Action<string>? onPlanChanged = null)
    {
        if (!Directory.Exists(plansDirectory)) return;

        foreach (var dir in Directory.GetDirectories(plansDirectory))
            try
            {
                MigratePlan(dir, onPlanChanged);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to migrate plan in {Folder}", Path.GetFileName(dir));
            }
    }

    /// <summary>
    ///     Brings a single plan folder up to <see cref="LatestVersion" />. Returns true if plan.yaml was
    ///     rewritten. Skips terminal plans, plans without a plan.yaml, and plans already at the latest version.
    /// </summary>
    public bool MigratePlan(string planFolder, Action<string>? onPlanChanged = null)
    {
        var planYamlPath = Path.Combine(planFolder, "plan.yaml");
        if (!File.Exists(planYamlPath)) return false;

        var yaml = FileHelper.ReadAllText(planYamlPath);

        var stateMatch = StateLineRegex.Match(yaml);
        var state = stateMatch.Success ? stateMatch.Groups[1].Value.Trim() : "";
        if (TerminalStates.Contains(state)) return false;

        var currentVersion = PlanSchemaVersion.Read(yaml);
        if (currentVersion >= LatestVersion) return false;

        var folderName = Path.GetFileName(planFolder);
        var migrated = yaml;
        foreach (var migration in _migrations.Where(m => m.Version > currentVersion).OrderBy(m => m.Version))
            migrated = migration.Apply(new PlanMigrationContext
            {
                PlanFolder = planFolder,
                FolderName = folderName,
                Yaml = migrated,
                State = state,
                Logger = _logger
            });

        // Always write — even when every migration was a no-op — so the stamp lands and the plan is
        // skipped on later startups.
        migrated = PlanSchemaVersion.Stamp(migrated, LatestVersion);
        FileHelper.WriteAllText(planYamlPath, migrated);
        _logger.LogInformation("Migrated plan {Folder} to schema version {Version}", folderName, LatestVersion);
        onPlanChanged?.Invoke(folderName);
        return true;
    }

    private static List<IPlanMigration> LoadMigrations()
    {
        var migrationType = typeof(IPlanMigration);
        var migrations = typeof(PlanMigrator).Assembly
            .GetTypes()
            .Where(t => migrationType.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .Select(t => (IPlanMigration)Activator.CreateInstance(t)!)
            .OrderBy(m => m.Version)
            .ToList();

        ValidateMigrationSequence(migrations);

        return migrations;
    }

    private static void ValidateMigrationSequence(List<IPlanMigration> migrations)
    {
        if (migrations.Count == 0) return;

        var duplicates = migrations.GroupBy(m => m.Version)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
            throw new InvalidOperationException(
                $"Duplicate plan migration versions found: {string.Join(", ", duplicates)}");

        for (var i = 0; i < migrations.Count; i++)
        {
            var expected = i + 1;
            var actual = migrations[i].Version;

            if (actual != expected)
                throw new InvalidOperationException(
                    $"Plan migration sequence is invalid. Expected version {expected}, found {actual}. " +
                    "Migrations must be numbered sequentially starting from 1.");
        }
    }
}
