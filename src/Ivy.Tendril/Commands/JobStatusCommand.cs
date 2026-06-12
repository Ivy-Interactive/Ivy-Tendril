using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Ivy.Tendril.Helpers;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class JobStatusSettings : CommandSettings
{
    [Description("Job ID (e.g., job-001)")]
    [CommandArgument(0, "<job-id>")]
    public string JobId { get; set; } = "";

    [Description("Status message to display")]
    [CommandOption("--message|-m")]
    public string Message { get; set; } = "";

    [Description("Plan ID to report")]
    [CommandOption("--plan-id")]
    public string? PlanId { get; set; }

    [Description("Plan title to report")]
    [CommandOption("--plan-title")]
    public string? PlanTitle { get; set; }

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.RequireNonEmpty(JobId, "job-id");
    }
}

public class JobStatusCommand : Command<JobStatusSettings>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    protected override int Execute(CommandContext context, JobStatusSettings settings, CancellationToken cancellationToken)
    {
        var discovery = MasterClient.Discover();
        using var client = MasterClient.CreateHttpClient(discovery);

        var payload = new { message = settings.Message, planId = settings.PlanId, planTitle = settings.PlanTitle };
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = client.PutAsync($"{discovery.BaseUrl}/api/jobs/{settings.JobId}/status", content, cancellationToken)
            .GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        Console.WriteLine($"Status updated for job {settings.JobId}");
        return 0;
    }
}
