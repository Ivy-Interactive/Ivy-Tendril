using System.Collections.Generic;
using System.Linq;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Xunit;

namespace Ivy.Tendril.Test;

public class VerificationSettingsTests
{
    [Fact]
    public void SaveVerification_EditingByName_CorrectlyUpdatesTargetDefinitionInVerifications()
    {
        var settings = new TendrilSettings
        {
            Verifications = new List<VerificationConfig>
            {
                new() { Name = "DotnetFormat", Prompt = "Format dotnet code" },
                new() { Name = "DotnetBuild", Prompt = "Build dotnet code" },
                new() { Name = "RustClippy", Prompt = "Run clippy" }
            },
            Projects = new List<ProjectConfig>
            {
                new()
                {
                    Name = "rustmc-server",
                    Verifications = new List<ProjectVerificationRef>
                    {
                        new() { Name = "RustClippy", Required = true }
                    }
                }
            }
        };

        // When user edits RustClippy (which is index 0 in project, but index 2 in global settings)
        VerificationSettingsHelper.SaveVerification(
            settings,
            existingVerificationName: "RustClippy",
            newName: "RustClippy",
            newPrompt: "Run cargo clippy --all-targets");

        // Verify global definitions: DotnetFormat is untouched, RustClippy is updated
        Assert.Equal("Format dotnet code", settings.Verifications.First(v => v.Name == "DotnetFormat").Prompt);
        Assert.Equal("Run cargo clippy --all-targets", settings.Verifications.First(v => v.Name == "RustClippy").Prompt);
    }

    [Fact]
    public void SaveVerification_RenamingVerification_UpdatesProjectVerificationReferencesInProjects()
    {
        var settings = new TendrilSettings
        {
            Verifications = new List<VerificationConfig>
            {
                new() { Name = "DotnetFormat", Prompt = "Format dotnet code" },
                new() { Name = "RustClippy", Prompt = "Run clippy" }
            },
            Projects = new List<ProjectConfig>
            {
                new()
                {
                    Name = "rustmc-server",
                    Verifications = new List<ProjectVerificationRef>
                    {
                        new() { Name = "RustClippy", Required = true }
                    }
                },
                new()
                {
                    Name = "rust-client",
                    Verifications = new List<ProjectVerificationRef>
                    {
                        new() { Name = "RustClippy", Required = false }
                    }
                }
            }
        };

        var projectVerificationsState = new List<ProjectVerificationRef>
        {
            new() { Name = "RustClippy", Required = true }
        };

        VerificationSettingsHelper.SaveVerification(
            settings,
            existingVerificationName: "RustClippy",
            newName: "RustCheck",
            newPrompt: "Run cargo check",
            projectName: "rustmc-server",
            projectVerifications: projectVerificationsState);

        // Target global verification is renamed
        Assert.DoesNotContain(settings.Verifications, v => v.Name == "RustClippy");
        var updated = settings.Verifications.FirstOrDefault(v => v.Name == "RustCheck");
        Assert.NotNull(updated);
        Assert.Equal("Run cargo check", updated.Prompt);

        // Project references are updated across all projects
        Assert.Equal("RustCheck", settings.Projects.First(p => p.Name == "rustmc-server").Verifications[0].Name);
        Assert.Equal("RustCheck", settings.Projects.First(p => p.Name == "rust-client").Verifications[0].Name);

        // Project state list is also updated
        Assert.Equal("RustCheck", projectVerificationsState[0].Name);
    }

    [Fact]
    public void SaveVerification_AddingNewVerification_AddsToGlobalAndProject()
    {
        var settings = new TendrilSettings
        {
            Verifications = new List<VerificationConfig>
            {
                new() { Name = "DotnetBuild", Prompt = "Build dotnet code" }
            },
            Projects = new List<ProjectConfig>
            {
                new()
                {
                    Name = "my-project",
                    Verifications = new List<ProjectVerificationRef>()
                }
            }
        };

        var projectVerificationsState = new List<ProjectVerificationRef>();

        VerificationSettingsHelper.SaveVerification(
            settings,
            existingVerificationName: null,
            newName: "NewCheck",
            newPrompt: "Prompt for new check",
            projectName: "my-project",
            projectVerifications: projectVerificationsState);

        Assert.Contains(settings.Verifications, v => v.Name == "NewCheck" && v.Prompt == "Prompt for new check");
        Assert.Contains(settings.Projects[0].Verifications, v => v.Name == "NewCheck");
        Assert.Contains(projectVerificationsState, v => v.Name == "NewCheck");
    }
}
