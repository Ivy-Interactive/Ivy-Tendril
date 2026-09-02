using System;
using System.Collections.Generic;
using System.Linq;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Helpers;

public static class VerificationSettingsHelper
{
    public static void SaveVerification(
        TendrilSettings settings,
        string? existingVerificationName,
        string newName,
        string newPrompt,
        string? projectName = null,
        List<ProjectVerificationRef>? projectVerifications = null)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;

        var isNew = string.IsNullOrEmpty(existingVerificationName);
        var trimmedName = newName.Trim();

        if (isNew)
        {
            var existing = settings.Verifications.FirstOrDefault(v => v.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                settings.Verifications.Add(new VerificationConfig
                {
                    Name = trimmedName,
                    Prompt = newPrompt
                });
            }
            else
            {
                existing.Prompt = newPrompt;
            }

            if (!string.IsNullOrEmpty(projectName))
            {
                var proj = settings.Projects.FirstOrDefault(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));
                if (proj != null && !proj.Verifications.Any(v => v.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase)))
                {
                    proj.Verifications.Add(new ProjectVerificationRef { Name = trimmedName, Required = true });
                }
            }

            if (projectVerifications != null && !projectVerifications.Any(v => v.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase)))
            {
                projectVerifications.Add(new ProjectVerificationRef { Name = trimmedName, Required = true });
            }
        }
        else
        {
            var target = settings.Verifications.FirstOrDefault(v => v.Name.Equals(existingVerificationName, StringComparison.OrdinalIgnoreCase));
            if (target == null) return;

            var oldName = target.Name;
            target.Name = trimmedName;
            target.Prompt = newPrompt;

            if (!oldName.Equals(trimmedName, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var proj in settings.Projects)
                {
                    foreach (var pv in proj.Verifications)
                    {
                        if (pv.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase))
                        {
                            pv.Name = trimmedName;
                        }
                    }
                }

                if (projectVerifications != null)
                {
                    foreach (var pv in projectVerifications)
                    {
                        if (pv.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase))
                        {
                            pv.Name = trimmedName;
                        }
                    }
                }
            }
        }
    }
}
