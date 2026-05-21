using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Claude;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Test.Runtime;

public class AgentValidatorTests
{
    private readonly AgentValidator _validator = new();
    private readonly ClaudeCli _cli = new();

    [Fact]
    public void Validate_ValidContext_ReturnsNoProblems()
    {
        var context = new AgentResolutionContext
        {
            Prompt = "Hello",
            WorkingDirectory = Path.GetTempPath(),
        };

        var problems = _validator.Validate(context, _cli);
        var errors = problems.Where(p => p.Severity == ValidationSeverity.Error).ToList();

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_EmptyPrompt_ReturnsError()
    {
        var context = new AgentResolutionContext
        {
            Prompt = "",
            WorkingDirectory = Path.GetTempPath(),
        };

        var problems = _validator.Validate(context, _cli);

        Assert.Contains(problems, p => p.Code == "MISSING_PROMPT");
    }

    [Fact]
    public void Validate_PromptFilePath_SatisfiesPromptRequirement()
    {
        var context = new AgentResolutionContext
        {
            Prompt = "",
            WorkingDirectory = Path.GetTempPath(),
            PromptFilePath = "/some/file.md",
        };

        var problems = _validator.Validate(context, _cli);

        Assert.DoesNotContain(problems, p => p.Code == "MISSING_PROMPT");
    }

    [Fact]
    public void Validate_EmptyWorkingDirectory_ReturnsError()
    {
        var context = new AgentResolutionContext
        {
            Prompt = "Hello",
            WorkingDirectory = "",
        };

        var problems = _validator.Validate(context, _cli);

        Assert.Contains(problems, p => p.Code == "MISSING_WORKING_DIR");
    }

    [Fact]
    public void Validate_NonExistentWorkingDirectory_ReturnsError()
    {
        var context = new AgentResolutionContext
        {
            Prompt = "Hello",
            WorkingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
        };

        var problems = _validator.Validate(context, _cli);

        Assert.Contains(problems, p => p.Code == "INVALID_WORKING_DIR");
    }

    [Fact]
    public void Validate_NegativeMaxTurns_ReturnsError()
    {
        var context = new AgentResolutionContext
        {
            Prompt = "Hello",
            WorkingDirectory = Path.GetTempPath(),
            MaxTurns = 0,
        };

        var problems = _validator.Validate(context, _cli);

        Assert.Contains(problems, p => p.Code == "INVALID_MAX_TURNS");
    }

    [Fact]
    public void Validate_ValidMaxTurns_NoError()
    {
        var context = new AgentResolutionContext
        {
            Prompt = "Hello",
            WorkingDirectory = Path.GetTempPath(),
            MaxTurns = 5,
        };

        var problems = _validator.Validate(context, _cli);

        Assert.DoesNotContain(problems, p => p.Code == "INVALID_MAX_TURNS");
    }

    [Fact]
    public void Validate_NegativeBudget_ReturnsError()
    {
        var context = new AgentResolutionContext
        {
            Prompt = "Hello",
            WorkingDirectory = Path.GetTempPath(),
            MaxBudgetUsd = -1m,
        };

        var problems = _validator.Validate(context, _cli);

        Assert.Contains(problems, p => p.Code == "INVALID_BUDGET");
    }

    [Fact]
    public void Validate_ZeroBudget_ReturnsError()
    {
        var context = new AgentResolutionContext
        {
            Prompt = "Hello",
            WorkingDirectory = Path.GetTempPath(),
            MaxBudgetUsd = 0m,
        };

        var problems = _validator.Validate(context, _cli);

        Assert.Contains(problems, p => p.Code == "INVALID_BUDGET");
    }

    [Fact]
    public void Validate_ToolConflict_ReturnsWarning()
    {
        var context = new AgentResolutionContext
        {
            Prompt = "Hello",
            WorkingDirectory = Path.GetTempPath(),
            AllowedTools = ["Read", "Write"],
            DeniedTools = ["Write", "Bash"],
        };

        var problems = _validator.Validate(context, _cli);

        var conflict = problems.FirstOrDefault(p => p.Code == "TOOL_CONFLICT");
        Assert.NotNull(conflict);
        Assert.Equal(ValidationSeverity.Warning, conflict.Severity);
        Assert.Contains("Write", conflict.Message);
    }

    [Fact]
    public void Validate_NoToolConflict_NoWarning()
    {
        var context = new AgentResolutionContext
        {
            Prompt = "Hello",
            WorkingDirectory = Path.GetTempPath(),
            AllowedTools = ["Read", "Write"],
            DeniedTools = ["Bash"],
        };

        var problems = _validator.Validate(context, _cli);

        Assert.DoesNotContain(problems, p => p.Code == "TOOL_CONFLICT");
    }

    [Fact]
    public void Validate_MultipleErrors_ReturnsAll()
    {
        var context = new AgentResolutionContext
        {
            Prompt = "",
            WorkingDirectory = "",
            MaxTurns = -1,
            MaxBudgetUsd = 0m,
        };

        var problems = _validator.Validate(context, _cli);
        var errors = problems.Where(p => p.Severity == ValidationSeverity.Error).ToList();

        Assert.True(errors.Count >= 3);
    }
}
