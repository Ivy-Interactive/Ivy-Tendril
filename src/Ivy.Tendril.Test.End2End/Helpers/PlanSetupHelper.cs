using System.Text.RegularExpressions;

namespace Ivy.Tendril.Test.End2End.Helpers;

public static class PlanSetupHelper
{
    public static string CreateDraftPlan(
        string plansDir,
        string title,
        string description,
        string project = "E2ETest",
        string[]? steps = null,
        string[]? verifications = null)
    {
        var planId = GetNextPlanId(plansDir);
        var safeName = ToCamelCase(title);
        var folderName = $"{planId}-{safeName}";
        var planFolder = Path.Combine(plansDir, folderName);

        Directory.CreateDirectory(planFolder);
        Directory.CreateDirectory(Path.Combine(planFolder, "revisions"));

        steps ??= [$"Implement: {description}"];
        verifications ??= ["DotnetBuild"];

        var stepsYaml = string.Join("\n", steps.Select(s => $"  - {s}"));
        var verificationsYaml = string.Join("\n", verifications.Select(v => $"  - name: {v}"));

        var planYaml = $"""
            title: "{title}"
            description: "{description}"
            state: Draft
            project: {project}
            priority: 0
            created: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}
            updated: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}
            steps:
            {stepsYaml}
            verifications:
            {verificationsYaml}
            """;

        File.WriteAllText(Path.Combine(planFolder, "plan.yaml"), planYaml);

        return planFolder;
    }

    public static string CreateReadyForReviewPlan(
        string plansDir,
        string title,
        string project = "E2ETest")
    {
        var planId = GetNextPlanId(plansDir);
        var safeName = ToCamelCase(title);
        var folderName = $"{planId}-{safeName}";
        var planFolder = Path.Combine(plansDir, folderName);

        Directory.CreateDirectory(planFolder);
        Directory.CreateDirectory(Path.Combine(planFolder, "revisions"));

        var planYaml = $"""
            title: "{title}"
            description: "Test plan for PR creation"
            state: ReadyForReview
            project: {project}
            priority: 0
            created: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}
            updated: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}
            steps:
              - Make a small change
            verifications:
              - name: DotnetBuild
            """;

        File.WriteAllText(Path.Combine(planFolder, "plan.yaml"), planYaml);

        return planFolder;
    }

    public static string CreatePlanWithState(
        string plansDir,
        string title,
        string state,
        string project = "E2ETest")
    {
        var planId = GetNextPlanId(plansDir);
        var safeName = ToCamelCase(title);
        var folderName = $"{planId}-{safeName}";
        var planFolder = Path.Combine(plansDir, folderName);

        Directory.CreateDirectory(planFolder);
        Directory.CreateDirectory(Path.Combine(planFolder, "revisions"));

        var planYaml = $"""
            title: "{title}"
            description: "Test plan"
            state: {state}
            project: {project}
            priority: 0
            created: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}
            updated: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}
            steps:
              - Step 1
              - Step 2
              - Step 3
            verifications:
              - name: DotnetBuild
            """;

        File.WriteAllText(Path.Combine(planFolder, "plan.yaml"), planYaml);

        return planFolder;
    }

    public static string GetNextPlanId(string plansDir)
    {
        Directory.CreateDirectory(plansDir);
        var counterFile = Path.Combine(plansDir, ".counter");

        int counter = 0;
        if (File.Exists(counterFile))
        {
            var text = File.ReadAllText(counterFile).Trim();
            int.TryParse(text, out counter);
        }

        counter++;
        File.WriteAllText(counterFile, counter.ToString());

        return counter.ToString("D5");
    }

    public static string GetPlanId(string planFolder)
    {
        var folderName = Path.GetFileName(planFolder);
        var dashIdx = folderName.IndexOf('-');
        return dashIdx > 0 ? folderName[..dashIdx] : folderName;
    }

    private static string ToCamelCase(string input)
    {
        var words = Regex.Split(input, @"[\s\-_]+")
            .Where(w => w.Length > 0)
            .Select(w => char.ToUpper(w[0]) + w[1..]);
        return string.Join("", words);
    }
}
