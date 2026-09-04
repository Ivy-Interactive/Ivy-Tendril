using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test.Helpers;

[Collection("EnvironmentTests")]
public class BetaHelperTests : IDisposable
{
    private readonly string? _originalTendrilBeta;
    private readonly string? _originalIvyBeta;

    public BetaHelperTests()
    {
        _originalTendrilBeta = Environment.GetEnvironmentVariable("TENDRIL_BETA");
        _originalIvyBeta = Environment.GetEnvironmentVariable("IVY_BETA");

        Environment.SetEnvironmentVariable("TENDRIL_BETA", null);
        Environment.SetEnvironmentVariable("IVY_BETA", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TENDRIL_BETA", _originalTendrilBeta);
        Environment.SetEnvironmentVariable("IVY_BETA", _originalIvyBeta);
    }

    [Fact]
    public void IsBeta_TendrilArgsBetaTrue_ReturnsTrue()
    {
        var args = new TendrilArgs { Beta = true };
        Assert.True(BetaHelper.IsBeta(tendrilArgs: args));
    }

    [Fact]
    public void IsBeta_ConfigSettingsBetaTrue_ReturnsTrue()
    {
        var settings = new TendrilSettings { Beta = true };
        Assert.True(BetaHelper.IsBeta(settings));
    }

    [Fact]
    public void IsBeta_TendrilBetaEnvVarSetToOne_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable("TENDRIL_BETA", "1");
        Assert.True(BetaHelper.IsBeta());
    }

    [Fact]
    public void IsBeta_IvyBetaEnvVarSetToOne_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable("IVY_BETA", "1");
        Assert.True(BetaHelper.IsBeta());
    }

    [Fact]
    public void IsBeta_AllDisabled_ReturnsFalse()
    {
        var args = new TendrilArgs { Beta = false };
        var settings = new TendrilSettings { Beta = false };

        Assert.False(BetaHelper.IsBeta(args, settings));
    }
}
