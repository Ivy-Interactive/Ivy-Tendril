using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Runtime;

public sealed class AgentValidator
{
    public IReadOnlyList<ValidationProblem> Validate(AgentResolutionContext context, IAgentCli cli)
    {
        var problems = new List<ValidationProblem>();

        if (string.IsNullOrWhiteSpace(context.Prompt) && string.IsNullOrWhiteSpace(context.PromptFilePath))
        {
            problems.Add(new ValidationProblem
            {
                Severity = ValidationSeverity.Error,
                Code = "MISSING_PROMPT",
                Message = "Either Prompt or PromptFilePath must be specified",
                PropertyName = nameof(context.Prompt),
            });
        }

        if (string.IsNullOrWhiteSpace(context.WorkingDirectory))
        {
            problems.Add(new ValidationProblem
            {
                Severity = ValidationSeverity.Error,
                Code = "MISSING_WORKING_DIR",
                Message = "WorkingDirectory is required",
                PropertyName = nameof(context.WorkingDirectory),
            });
        }
        else if (!Directory.Exists(context.WorkingDirectory))
        {
            problems.Add(new ValidationProblem
            {
                Severity = ValidationSeverity.Error,
                Code = "INVALID_WORKING_DIR",
                Message = $"Working directory does not exist: {context.WorkingDirectory}",
                PropertyName = nameof(context.WorkingDirectory),
            });
        }

        var binaryName = cli.Id switch
        {
            AgentId.Antigravity => "agy",
            AgentId.Claude => "claude",
            AgentId.Copilot => Providers.Copilot.CopilotBinaryResolver.Resolve().FileName,
            _ => cli.Id
        };
        if (!BinaryResolver.IsInstalled(binaryName))
        {
            problems.Add(new ValidationProblem
            {
                Severity = ValidationSeverity.Error,
                Code = "BINARY_NOT_FOUND",
                Message = $"Agent binary '{cli.Id}' not found on PATH",
            });
        }

        if (context.MaxTurns is < 1)
        {
            problems.Add(new ValidationProblem
            {
                Severity = ValidationSeverity.Error,
                Code = "INVALID_MAX_TURNS",
                Message = "MaxTurns must be at least 1",
                PropertyName = nameof(context.MaxTurns),
            });
        }

        if (context.MaxBudgetUsd is <= 0)
        {
            problems.Add(new ValidationProblem
            {
                Severity = ValidationSeverity.Error,
                Code = "INVALID_BUDGET",
                Message = "MaxBudgetUsd must be positive",
                PropertyName = nameof(context.MaxBudgetUsd),
            });
        }

        if (context.AllowedTools.Count > 0 && context.DeniedTools.Count > 0)
        {
            var overlap = context.AllowedTools.Intersect(context.DeniedTools, StringComparer.OrdinalIgnoreCase).ToList();
            if (overlap.Count > 0)
            {
                problems.Add(new ValidationProblem
                {
                    Severity = ValidationSeverity.Warning,
                    Code = "TOOL_CONFLICT",
                    Message = $"Tools appear in both AllowedTools and DeniedTools: {string.Join(", ", overlap)}",
                });
            }
        }

        return problems;
    }
}
