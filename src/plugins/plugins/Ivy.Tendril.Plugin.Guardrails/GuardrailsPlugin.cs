using System.Text.Json;
using Ivy.Plugins;
using Ivy.Plugins.Hooks;
using Ivy.Plugins.Inbox;

[assembly: IvyPlugin(typeof(Ivy.Tendril.Plugin.Guardrails.GuardrailsPlugin))]

namespace Ivy.Tendril.Plugin.Guardrails;

public class GuardrailsPlugin : IIvyPlugin<ITendrilPluginContext>
{
    private readonly GuardrailsState _state = new();

    public PluginManifest Manifest { get; } = new()
    {
        Id = "Ivy.Tendril.Plugin.Guardrails",
        Title = "Project Guardrails",
        Version = new Version(1, 0, 0),
        Icon = PluginIcon.Named("Shield"),
    };

    public PluginConfigurationSchema ConfigurationSchema { get; } = new SchemaBuilder()
        .AddInteger("FailureThreshold", defaultValue: 3, description: "Consecutive job failures before alerting that a project is degraded")
        .AddBoolean("EnableLockCheck", defaultValue: true, description: "Block jobs when a .tendril-lock file is present")
        .AddBoolean("EnableGuardrailsEnrichment", defaultValue: true, description: "Prepend .tendril-guardrails content to plan descriptions")
        .Build();

    public void Configure(ITendrilPluginContext context)
    {
        var tendrilHome = context.TendrilHome;
        var inbox = context.Inbox;
        var config = context.Config;

        var failureThreshold = config.GetInt("FailureThreshold") ?? 3;
        var enableLockCheck = config.GetValue("EnableLockCheck") != "false";
        var enableEnrichment = config.GetValue("EnableGuardrailsEnrichment") != "false";

        string GuardrailsDir(string project) =>
            Path.Combine(tendrilHome, "guardrails", project);

        // ── BeforeJob: Lock file check ──────────────────────────────────────
        context.Hooks.BeforeJob(async (evt, ct) =>
        {
            if (!enableLockCheck) return;

            var lockFile = Path.Combine(GuardrailsDir(evt.Project), ".tendril-lock");
            if (!File.Exists(lockFile)) return;

            var reason = await File.ReadAllTextAsync(lockFile, ct);
            evt.Cancel(string.IsNullOrWhiteSpace(reason)
                ? $"Project '{evt.Project}' is locked"
                : $"Project '{evt.Project}' is locked: {reason.Trim()}");
        });

        // ── AfterJob: Failure tracking + degradation alert ──────────────────
        context.Hooks.AfterJob(async (evt, ct) =>
        {
            _state.RecordJob(evt.Project, evt.Status);

            if (_state.IsProjectDegraded(evt.Project, failureThreshold))
            {
                inbox.Add(new InboxItem
                {
                    Description = $"⚠️ Project '{evt.Project}' appears degraded — " +
                                  $"the last {failureThreshold} jobs all failed. " +
                                  $"Please investigate recent changes.",
                    Project = evt.Project,
                    Labels = ["guardrails", "degraded"],
                });
            }

            await Task.CompletedTask;
        });

        // ── BeforeCreatePlan: Guardrails enrichment ─────────────────────────
        context.Hooks.BeforeCreatePlan(async (evt, ct) =>
        {
            if (!enableEnrichment) return;

            var guardrailsFile = Path.Combine(GuardrailsDir(evt.Project), ".tendril-guardrails");
            if (!File.Exists(guardrailsFile)) return;

            var guardrails = await File.ReadAllTextAsync(guardrailsFile, ct);
            if (!string.IsNullOrWhiteSpace(guardrails))
            {
                evt.Description = $"[Project Guardrails]\n{guardrails.Trim()}\n\n[Task]\n{evt.Description}";
            }
        });

        // ── AfterCreatePlan: Audit log ──────────────────────────────────────
        context.Hooks.AfterCreatePlan(async (evt, ct) =>
        {
            var auditFile = Path.Combine(tendrilHome, "guardrails-audit.jsonl");
            var entry = JsonSerializer.Serialize(new
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                planId = evt.PlanId,
                planFolder = evt.PlanFolder,
                project = evt.Project,
            });

            await File.AppendAllTextAsync(auditFile, entry + Environment.NewLine, ct);
        });

        // ── BeforeConfigSave: Basic validation ──────────────────────────────
        context.Hooks.BeforeConfigSave(evt =>
        {
            var json = JsonSerializer.Serialize(evt.NewSettings);
            if (json.Contains("INVALID", StringComparison.OrdinalIgnoreCase))
            {
                evt.Reject("Config contains 'INVALID' marker — refusing to save.");
            }
        });

        // ── AfterConfigReload: Reset cached state ───────────────────────────
        context.Hooks.AfterConfigReload(() =>
        {
            _state.Reset();
        });
    }
}
