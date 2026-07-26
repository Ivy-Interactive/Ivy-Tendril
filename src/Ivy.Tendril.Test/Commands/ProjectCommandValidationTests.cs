using Ivy.Tendril.Commands;

namespace Ivy.Tendril.Test.Commands;

public class ProjectCommandValidationTests
{
    [Fact]
    public void ProjectAddSettings_Validate_SlashedName_Fails()
    {
        var settings = new ProjectAddSettings { Name = "foo/bar" };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("letters, digits, dots, dashes and underscores", result.Message);
    }

    [Fact]
    public void ProjectAddSettings_Validate_ValidName_Succeeds()
    {
        var settings = new ProjectAddSettings { Name = "Ivy-Tendril" };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }

    [Fact]
    public void ProjectSetSettings_Validate_NameFieldWithSlash_Fails()
    {
        var settings = new ProjectSetSettings { Name = "p", Field = "name", Value = "foo/bar" };

        var result = settings.Validate();

        Assert.False(result.Successful);
    }

    [Fact]
    public void ProjectSetSettings_Validate_StackHashFieldWithSlashes_Succeeds()
    {
        var settings = new ProjectSetSettings
        {
            Name = "p",
            Field = "stackHash",
            Value = "fe.ts:react+next/be.py:fastapi/db:postgres"
        };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }
}
