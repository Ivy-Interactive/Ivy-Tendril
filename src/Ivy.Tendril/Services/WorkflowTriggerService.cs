using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Jobs;
using Ivy.Tendril.Services.Plans;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

public class WorkflowTriggerService : IStartable, IDisposable
{
    private readonly IPlanDatabaseService _database;
    private readonly IJobService _jobService;
    private readonly IPlanReaderService _planReader;
    private readonly IPlanWatcherService _planWatcher;
    private readonly ILogger<WorkflowTriggerService> _logger;

    private Timer? _timer;
    private DateTime? _lastTriggeredMinute;
    private readonly HashSet<int> _triggeredPlanIds = new();
    private readonly object _lock = new();

    public WorkflowTriggerService(
        IPlanDatabaseService database,
        IJobService jobService,
        IPlanReaderService planReader,
        IPlanWatcherService planWatcher,
        ILogger<WorkflowTriggerService> _loggerInstance)
    {
        _database = database;
        _jobService = jobService;
        _planReader = planReader;
        _planWatcher = planWatcher;
        _logger = _loggerInstance;
    }

    public void Start()
    {
        _logger.LogInformation("Starting Workflow Trigger Service...");

        // Initialize triggered plans set to avoid duplicate runs for already completed/merged plans
        InitializeTriggeredPlans();

        // Check every 10 seconds to align with minute boundary
        _timer = new Timer(_ => CheckTimedTriggers(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));

        // Listen for workspace changes to handle event triggers
        _planWatcher.PlansChanged += OnPlansChanged;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _planWatcher.PlansChanged -= OnPlansChanged;
    }

    private void InitializeTriggeredPlans()
    {
        lock (_lock)
        {
            try
            {
                var plans = _planReader.GetPlans();
                var prStatuses = _database.GetAllPrStatuses();
                foreach (var plan in plans)
                {
                    if (plan.Status == PlanStatus.Completed)
                    {
                        var allMerged = plan.Prs.Count > 0;
                        if (plan.Prs.Count > 0)
                        {
                            foreach (var url in plan.Prs)
                            {
                                if (!prStatuses.TryGetValue(url, out var status) ||
                                    !string.Equals(status, "Merged", StringComparison.OrdinalIgnoreCase))
                                {
                                    allMerged = false;
                                    break;
                                }
                            }
                        }

                        if (allMerged)
                        {
                            _triggeredPlanIds.Add(plan.Id);
                        }
                    }
                }
                _logger.LogInformation("Initialized Workflow Trigger Service with {Count} already triggered plans.", _triggeredPlanIds.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize already completed and merged plans list.");
            }
        }
    }

    private void CheckTimedTriggers()
    {
        var now = DateTime.UtcNow;
        var currentMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);

        if (_lastTriggeredMinute.HasValue && _lastTriggeredMinute.Value == currentMinute)
        {
            return;
        }

        _lastTriggeredMinute = currentMinute;
        _logger.LogDebug("Evaluating timed triggers for {Minute:yyyy-MM-dd HH:mm:ss} UTC", currentMinute);

        try
        {
            var workflows = _database.GetWorkflows();
            foreach (var wf in workflows)
            {
                if (!wf.IsActive) continue;

                WorkflowDefinition def;
                try
                {
                    def = JsonSerializer.Deserialize<WorkflowDefinition>(wf.Definition, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                          ?? new WorkflowDefinition();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse definition for workflow '{WorkflowName}' (ID {WorkflowId})", wf.Name, wf.Id);
                    continue;
                }

                foreach (var step in def.Steps)
                {
                    if (step.Type.Equals("Trigger", StringComparison.OrdinalIgnoreCase) &&
                        step.Action.Equals("schedule", StringComparison.OrdinalIgnoreCase))
                    {
                        var cronExpression = step.Args;
                        if (CronMatcher.Matches(cronExpression, currentMinute))
                        {
                            _logger.LogInformation("Timed trigger fired for workflow '{WorkflowName}' (ID {WorkflowId}) on expression '{Cron}'", wf.Name, wf.Id, cronExpression);
                            
                            var payload = new
                            {
                                Trigger = "schedule",
                                Cron = cronExpression,
                                Timestamp = currentMinute.ToString("O")
                            };
                            var payloadJson = JsonSerializer.Serialize(payload);
                            
                            var runArgs = new WorkflowRunArgs(wf.Id, payloadJson, wf.Project);
                            _jobService.StartJob(runArgs);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking timed triggers");
        }
    }

    private void OnPlansChanged(string? folderName)
    {
        if (string.IsNullOrEmpty(folderName))
        {
            // Trigger check for all plans if directory modified
            CheckAllPlansForEventTriggers();
            return;
        }

        try
        {
            var planPath = Path.Combine(_planReader.PlansDirectory, folderName);
            var plan = _planReader.GetPlanByFolder(planPath);
            if (plan != null)
            {
                CheckPlanForEventTriggers(plan);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing event triggers for plan folder: {Folder}", folderName);
        }
    }

    private void CheckAllPlansForEventTriggers()
    {
        try
        {
            var plans = _planReader.GetPlans();
            foreach (var plan in plans)
            {
                CheckPlanForEventTriggers(plan);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking all plans for event triggers");
        }
    }

    private void CheckPlanForEventTriggers(PlanFile plan)
    {
        if (plan.Status == PlanStatus.Completed)
        {
            CheckAndTriggerPlanCompletedAndMergedInternal(plan);
        }
    }

    public void CheckAndTriggerPlanCompletedAndMerged(string prUrl)
    {
        try
        {
            var plans = _planReader.GetPlans();
            var matchingPlans = plans.Where(p => p.Prs.Contains(prUrl, StringComparer.OrdinalIgnoreCase)).ToList();

            foreach (var plan in matchingPlans)
            {
                CheckAndTriggerPlanCompletedAndMergedInternal(plan);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking completed and merged state after PR update for: {PrUrl}", prUrl);
        }
    }

    private void CheckAndTriggerPlanCompletedAndMergedInternal(PlanFile plan)
    {
        lock (_lock)
        {
            if (_triggeredPlanIds.Contains(plan.Id))
            {
                return;
            }

            if (plan.Status != PlanStatus.Completed)
            {
                return;
            }

            var prStatuses = _database.GetAllPrStatuses();
            var allMerged = plan.Prs.Count > 0;

            if (plan.Prs.Count > 0)
            {
                foreach (var url in plan.Prs)
                {
                    if (prStatuses.TryGetValue(url, out var status))
                    {
                        if (!string.Equals(status, "Merged", StringComparison.OrdinalIgnoreCase))
                        {
                            allMerged = false;
                            break;
                        }
                    }
                    else
                    {
                        allMerged = false;
                        break;
                    }
                }
            }

            if (allMerged)
            {
                _triggeredPlanIds.Add(plan.Id);
                _logger.LogInformation("Workflow trigger: Plan '{PlanTitle}' ({PlanFolder}) completed and merged! Triggering event.", plan.Title, plan.FolderName);

                var payload = new
                {
                    Event = "plan_completed_and_merged",
                    PlanId = plan.Id,
                    PlanTitle = plan.Title,
                    PlanFolder = plan.FolderName,
                    Project = plan.Project,
                    Prs = plan.Prs
                };
                var payloadJson = JsonSerializer.Serialize(payload);

                TriggerEvent("plan_completed_and_merged", plan.Project, payloadJson);
            }
        }
    }

    public void TriggerEvent(string eventType, string project, string payloadJson)
    {
        try
        {
            var workflows = _database.GetWorkflows(project);
            foreach (var wf in workflows)
            {
                if (!wf.IsActive) continue;

                WorkflowDefinition def;
                try
                {
                    def = JsonSerializer.Deserialize<WorkflowDefinition>(wf.Definition, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                          ?? new WorkflowDefinition();
                }
                catch
                {
                    continue;
                }

                foreach (var step in def.Steps)
                {
                    if (step.Type.Equals("Trigger", StringComparison.OrdinalIgnoreCase) &&
                        step.Action.Equals("event", StringComparison.OrdinalIgnoreCase) &&
                        step.Args.Equals(eventType, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("Triggering workflow '{WorkflowName}' (ID {WorkflowId}) on event '{Event}'", wf.Name, wf.Id, eventType);
                        var runArgs = new WorkflowRunArgs(wf.Id, payloadJson, wf.Project);
                        _jobService.StartJob(runArgs);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger event '{Event}' for project '{Project}'", eventType, project);
        }
    }
}

public static class CronMatcher
{
    public static bool Matches(string expression, DateTime dt)
    {
        if (string.IsNullOrWhiteSpace(expression)) return false;

        var parts = expression.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5) return false;

        return MatchField(parts[0], dt.Minute, 0, 59) &&
               MatchField(parts[1], dt.Hour, 0, 23) &&
               MatchField(parts[2], dt.Day, 1, 31) &&
               MatchField(parts[3], dt.Month, 1, 12) &&
               MatchField(parts[4], (int)dt.DayOfWeek, 0, 6);
    }

    private static bool MatchField(string field, int value, int min, int max)
    {
        if (field == "*") return true;

        var parts = field.Split(',');
        foreach (var part in parts)
        {
            if (part.StartsWith("*/"))
            {
                if (int.TryParse(part.Substring(2), out var step) && step > 0)
                {
                    if (value % step == 0) return true;
                }
            }
            else if (part.Contains('-'))
            {
                var rangeParts = part.Split('-');
                if (rangeParts.Length == 2 &&
                    int.TryParse(rangeParts[0], out var rangeMin) &&
                    int.TryParse(rangeParts[1], out var rangeMax))
                {
                    var valToCheck = value;
                    if (min == 0 && max == 6)
                    {
                        if (rangeMin == 7) rangeMin = 0;
                        if (rangeMax == 7) rangeMax = 0;
                    }
                    if (valToCheck >= rangeMin && valToCheck <= rangeMax) return true;
                }
            }
            else
            {
                if (int.TryParse(part, out var exactVal))
                {
                    if (min == 0 && max == 6 && exactVal == 7) exactVal = 0;
                    if (value == exactVal) return true;
                }
            }
        }

        return false;
    }
}
