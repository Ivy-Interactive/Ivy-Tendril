using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Test.Abstractions;

public class ValidationTypesTests
{
    [Fact]
    public void ValidationProblem_Error_CreatesCorrectly()
    {
        var problem = new ValidationProblem
        {
            Severity = ValidationSeverity.Error,
            Code = "MISSING_FIELD",
            Message = "Prompt is required",
            PropertyName = "Prompt",
        };

        Assert.Equal(ValidationSeverity.Error, problem.Severity);
        Assert.Equal("MISSING_FIELD", problem.Code);
        Assert.Equal("Prompt is required", problem.Message);
        Assert.Equal("Prompt", problem.PropertyName);
    }

    [Fact]
    public void ValidationProblem_Warning_NoProperty()
    {
        var problem = new ValidationProblem
        {
            Severity = ValidationSeverity.Warning,
            Code = "HIGH_COST",
            Message = "This model is expensive",
        };

        Assert.Null(problem.PropertyName);
    }

    [Theory]
    [InlineData(ValidationSeverity.Error)]
    [InlineData(ValidationSeverity.Warning)]
    [InlineData(ValidationSeverity.Info)]
    public void ValidationSeverity_AllValues_Exist(ValidationSeverity severity)
    {
        var problem = new ValidationProblem
        {
            Severity = severity,
            Code = "TEST",
            Message = "test",
        };
        Assert.Equal(severity, problem.Severity);
    }
}
