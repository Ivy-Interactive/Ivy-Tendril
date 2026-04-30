using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Test.Helpers;

public class VariableExpansionTests
{
    [Fact]
    public void InitializeUserSecrets_WithUserSecretsIdAttribute_DoesNotThrow()
    {
        // Act - This will use the entry assembly which should have UserSecretsIdAttribute
        // In test context, the entry assembly may not have UserSecretsIdAttribute,
        // but the method should handle this gracefully without throwing
        var exception = Record.Exception(() => VariableExpansion.InitializeUserSecrets(null));

        // Assert - Should not throw regardless of whether attribute is present
        Assert.Null(exception);
    }

    [Fact]
    public void ExpandDotnetUserSecrets_WithInitializedSecrets_DoesNotThrow()
    {
        // Arrange
        VariableExpansion.InitializeUserSecrets(null);

        // Act - Test that expansion doesn't throw
        // We can't test the actual expansion without setting up real user secrets,
        // but we can verify the method doesn't crash
        var exception = Record.Exception(() =>
            VariableExpansion.ExpandVariables("%DotnetUserSecrets:Test:Key%", null));

        // Assert - Should not throw
        Assert.Null(exception);
    }

    [Fact]
    public void ExpandDotnetUserSecrets_ReturnsNonNullValue()
    {
        // Act - Expand a DotnetUserSecrets reference
        var input = "%DotnetUserSecrets:Section:Key%";
        var result = VariableExpansion.ExpandVariables(input, null);

        // Assert - Should return a non-null value (either expanded or original)
        Assert.NotNull(result);
    }

    [Fact]
    public void InitializeUserSecrets_MultipleCallsDoNotThrow()
    {
        // Act - Call InitializeUserSecrets multiple times
        var exception = Record.Exception(() =>
        {
            VariableExpansion.InitializeUserSecrets(null);
            VariableExpansion.InitializeUserSecrets(null);
            VariableExpansion.InitializeUserSecrets(null);
        });

        // Assert - Should handle multiple initializations gracefully
        Assert.Null(exception);
    }

    [Fact]
    public void ExpandVariables_ExpandsTendrilHome()
    {
        // Arrange
        var testPath = Path.Combine("test", "path");

        // Act
        var result = VariableExpansion.ExpandVariables("%TENDRIL_HOME%", testPath);

        // Assert
        Assert.Equal(testPath, result);
    }

    [Fact]
    public void ExpandVariables_ExpandsEnvironmentVariables()
    {
        // Arrange
        var envVarName = "TEST_VAR_" + Guid.NewGuid().ToString("N");
        var envVarValue = "test_value_123";
        Environment.SetEnvironmentVariable(envVarName, envVarValue);

        try
        {
            // Act
            var result = VariableExpansion.ExpandVariables($"%{envVarName}%", null);

            // Assert
            Assert.Equal(envVarValue, result);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, null);
        }
    }
}
