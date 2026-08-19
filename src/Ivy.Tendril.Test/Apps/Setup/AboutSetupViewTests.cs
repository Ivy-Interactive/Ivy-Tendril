using Ivy.Tendril.Apps.Settings;

namespace Ivy.Tendril.Test.Apps.Setup;

public class AboutSetupViewTests
{
    [Fact]
    public void BundledToolInfo_ConstructsWithValidProperties()
    {
        var toolInfo = new AboutSetupView.BundledToolInfo(
            Tool: "OpenCode CLI",
            Status: "Bundled",
            License: "MIT License",
            Version: "1.0.0",
            Location: "/path/to/opencode");

        Assert.Equal("OpenCode CLI", toolInfo.Tool);
        Assert.Equal("Bundled", toolInfo.Status);
        Assert.Equal("MIT License", toolInfo.License);
        Assert.Equal("1.0.0", toolInfo.Version);
        Assert.Equal("/path/to/opencode", toolInfo.Location);
    }

    [Fact]
    public void GetDefaultToolchain_ContainsExpectedToolsAndLicenses()
    {
        var tools = AboutSetupView.GetDefaultToolchain(
            isIvyAgentBundled: true,
            ivyAgentPath: "/bin/ivy-agent",
            ivyAgentVersion: "1.0.0",
            isDotnetBundled: true,
            dotnetPath: "/bin/dotnet",
            dotnetVersion: "10.0.100",
            isPwshBundled: false,
            pwshPath: "/bin/pwsh",
            pwshVersion: "7.4.0",
            gitVersion: "2.40.0");

        Assert.NotNull(tools);
        Assert.Equal(4, tools.Length);

        var openCode = Assert.Single(tools, t => t.Tool == "OpenCode CLI");
        Assert.Equal("Bundled", openCode.Status);
        Assert.Equal("MIT License", openCode.License);
        Assert.Equal("1.0.0", openCode.Version);
        Assert.Equal("/bin/ivy-agent", openCode.Location);

        var dotnet = Assert.Single(tools, t => t.Tool == ".NET SDK / Runtime");
        Assert.Equal("Bundled SDK", dotnet.Status);
        Assert.Equal("MIT License", dotnet.License);
        Assert.Equal("10.0.100", dotnet.Version);
        Assert.Equal("/bin/dotnet", dotnet.Location);

        var pwsh = Assert.Single(tools, t => t.Tool == "PowerShell");
        Assert.Equal("MIT License", pwsh.License);
        Assert.Equal("7.4.0", pwsh.Version);
        Assert.Equal("/bin/pwsh", pwsh.Location);

        var git = Assert.Single(tools, t => t.Tool == "Git");
        Assert.Equal("System Tool", git.Status);
        Assert.Equal("GPL v2", git.License);
        Assert.Equal("2.40.0", git.Version);
        Assert.Equal("git", git.Location);
    }

    [Fact]
    public void GetDefaultToolchain_WhenNotBundled_HandlesStatusCorrectly()
    {
        var tools = AboutSetupView.GetDefaultToolchain(
            isIvyAgentBundled: false,
            ivyAgentPath: "/nonexistent/ivy-agent",
            ivyAgentVersion: "Not configured",
            isDotnetBundled: false,
            dotnetPath: "/system/dotnet",
            dotnetVersion: "10.0.100",
            isPwshBundled: false,
            pwshPath: "/nonexistent/pwsh",
            pwshVersion: "Not configured",
            gitVersion: "Not available");

        Assert.NotNull(tools);

        var openCode = Assert.Single(tools, t => t.Tool == "OpenCode CLI");
        Assert.Equal("Not Found", openCode.Status);
        Assert.Equal("MIT License", openCode.License);

        var dotnet = Assert.Single(tools, t => t.Tool == ".NET SDK / Runtime");
        Assert.Equal("Runtime", dotnet.Status);
        Assert.Equal("MIT License", dotnet.License);

        var pwsh = Assert.Single(tools, t => t.Tool == "PowerShell");
        Assert.Equal("Not Found", pwsh.Status);
        Assert.Equal("MIT License", pwsh.License);
    }

    [Fact]
    public void AboutSetupView_CanBeInstantiated()
    {
        var view = new AboutSetupView();
        Assert.NotNull(view);
    }
}
