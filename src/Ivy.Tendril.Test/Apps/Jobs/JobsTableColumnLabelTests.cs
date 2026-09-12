using Ivy.Tendril.Models;
using Xunit;

namespace Ivy.Tendril.Test.Apps.Jobs;

public class JobsTableColumnLabelTests
{
    private static readonly Dictionary<string, string> ExpectedLabels = new()
    {
        { nameof(JobItemRow.Id), "Id" },
        { nameof(JobItemRow.Status), "Status" },
        { nameof(JobItemRow.PlanId), "Plan Id" },
        { nameof(JobItemRow.Prompt), "Prompt" },
        { nameof(JobItemRow.Type), "Type" },
        { nameof(JobItemRow.Project), "Project" },
        { nameof(JobItemRow.Timer), "Timer" },
        { nameof(JobItemRow.AgentOutput), "Agent Output" },
        { nameof(JobItemRow.Cost), "Cost" },
        { nameof(JobItemRow.Tokens), "Tokens" },
        { nameof(JobItemRow.Timestamp), "Timestamp" },
        { nameof(JobItemRow.StatusMessage), "Status Message" },
        { nameof(JobItemRow.ErrorContext), "Error Context" }
    };

    [Fact]
    public void EveryLabelStripsBackToItsFilterToken()
    {
        foreach (var (propertyName, expectedLabel) in ExpectedLabels)
        {
            var labelWithoutSpaces = expectedLabel.Replace(" ", "");
            Assert.Equal(propertyName, labelWithoutSpaces);
        }
    }

    [Fact]
    public void FrameworkDerivesTheExpectedLabels()
    {
        foreach (var (propertyName, expectedLabel) in ExpectedLabels)
        {
            var property = typeof(JobItemRow).GetProperty(propertyName);
            Assert.NotNull(property);

            var derivedLabel = Ivy.StringHelper.LabelFor(propertyName, property.PropertyType);
            Assert.Equal(expectedLabel, derivedLabel);
        }
    }

    [Fact]
    public void ExpectedLabelsCoverEveryProperty()
    {
        var actualProperties = typeof(JobItemRow)
            .GetProperties()
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToArray();

        var expectedProperties = ExpectedLabels.Keys
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(expectedProperties, actualProperties);
    }

    [Fact]
    public void PromptColumnIsNamedForWhatItHolds()
    {
        Assert.NotNull(typeof(JobItemRow).GetProperty("Prompt"));
        Assert.Null(typeof(JobItemRow).GetProperty("Plan"));
    }

    [Fact]
    public void TimestampIsTheLastVisibleColumn()
    {
        // Ivy derives column order from JobItemRow's property declaration order, so this order IS
        // the rendered order (issue #2636).
        var declared = typeof(JobItemRow).GetProperties().Select(p => p.Name).ToArray();

        Assert.Equal(new[]
        {
            nameof(JobItemRow.Id),            // hidden
            nameof(JobItemRow.Status),
            nameof(JobItemRow.PlanId),
            nameof(JobItemRow.Prompt),
            nameof(JobItemRow.Type),
            nameof(JobItemRow.Project),
            nameof(JobItemRow.Timer),
            nameof(JobItemRow.AgentOutput),
            nameof(JobItemRow.Cost),
            nameof(JobItemRow.Tokens),
            nameof(JobItemRow.Timestamp),
            nameof(JobItemRow.StatusMessage),
            nameof(JobItemRow.ErrorContext)    // hidden
        }, declared);
    }
}
