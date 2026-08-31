using Ivy.Tendril.Services.Share;

namespace Ivy.Tendril.Test.Services;

public class AnonymousPersonaGeneratorTests
{
    [Fact]
    public void Generate_WithSameSeed_IsDeterministic()
    {
        var seed = "session-test-12345";
        var persona1 = AnonymousPersonaGenerator.Generate(seed);
        var persona2 = AnonymousPersonaGenerator.Generate(seed);

        Assert.NotNull(persona1);
        Assert.NotEmpty(persona1);
        Assert.Equal(persona1, persona2);
    }

    [Fact]
    public void Generate_WithDifferentSeeds_ProducesVariedPersonas()
    {
        var persona1 = AnonymousPersonaGenerator.Generate("seed-alpha");
        var persona2 = AnonymousPersonaGenerator.Generate("seed-beta");

        Assert.NotNull(persona1);
        Assert.NotNull(persona2);
        Assert.NotEqual(persona1, persona2);
    }

    [Fact]
    public void Generate_MatchesExpectedFormat()
    {
        for (int i = 0; i < 50; i++)
        {
            var persona = AnonymousPersonaGenerator.Generate($"seed-{i}");
            var parts = persona.Split(' ');
            Assert.Equal(2, parts.Length);
            Assert.True(char.IsUpper(parts[0][0]), $"Adjective {parts[0]} should start with upper case");
            Assert.True(char.IsUpper(parts[1][0]), $"Animal {parts[1]} should start with upper case");
        }
    }

    [Fact]
    public void Generate_WithoutSeed_ReturnsValidRandomPersona()
    {
        var persona = AnonymousPersonaGenerator.Generate();
        Assert.NotNull(persona);
        var parts = persona.Split(' ');
        Assert.Equal(2, parts.Length);
    }
}
