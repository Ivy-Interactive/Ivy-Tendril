using System.ComponentModel;
using System.Diagnostics;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PromptwareRunSettings : CommandSettings
{
    [Description("Promptware name (e.g., UpdateProject)")]
    [CommandArgument(0, "<promptware>")]
    public string Promptware { get; set; } = "";

    [Description("Free-form arguments passed to the promptware as the Args firmware value")]
    [CommandArgument(1, "[args]")]
    public string[] Args { get; set; } = [];

    [CommandOption("--profile")]
    [Description("Override the agent profile (e.g., deep, balanced, quick)")]
    public string? Profile { get; init; }

    [CommandOption("--working-dir")]
    [Description("Working directory for the agent process")]
    public string? WorkingDir { get; init; }

    [CommandOption("--value")]
    [Description("Additional firmware header values (key=value, repeatable)")]
    public string[]? Values { get; init; }

    [CommandOption("--plan")]
    [Description("Plan ID or folder path — populates TendrilPlanFolder, TendrilPlansFolder, TendrilPlanId")]
    public string? Plan { get; init; }

    [CommandOption("--promptware-path")]
    [Description("Additional directory to search for promptware folders")]
    public string? PromptwarePath { get; init; }

    [CommandOption("--config")]
    [Description("Override config.yaml path (for testing)")]
    public string? ConfigPath { get; init; }

    [CommandOption("--agent")]
    [Description("Override agent provider (claude, antigravity, codex, copilot, opencode)")]
    public string? Agent { get; init; }

    [CommandOption("--cli-log")]
    [Description("Path to write CLI invocation log (JSONL) — tracks tendril calls made by the agent")]
    public string? CliLog { get; init; }

    [CommandOption("--dry-run")]
    [Description("Print the compiled firmware and exit without launching the agent")]
    public bool DryRun { get; init; }
}

public class PromptwareRunCommand : Command<PromptwareRunSettings>
{
    private readonly IAgentRunner _agentRunner;
    private readonly ILogger<PromptwareRunCommand> _logger;

    public PromptwareRunCommand(IAgentRunner agentRunner, ILogger<PromptwareRunCommand> logger)
    {
        _agentRunner = agentRunner;
        _logger = logger;
    }

    protected override int Execute(CommandContext context, PromptwareRunSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            return Run(settings, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run promptware {Promptware}", settings.Promptware);
            return 1;
        }
    }


    internal int Run(PromptwareRunSettings settings, CancellationToken cancellationToken = default)
    {
        var configService = !string.IsNullOrEmpty(settings.ConfigPath)
            ? CreateConfigFromPath(settings.ConfigPath)
            : new ConfigService();

        var tendrilSettings = configService.Settings;

        var programFolder = PromptwareHelper.ResolvePromptwareFolder(settings.Promptware, configService.TendrilHome, settings.PromptwarePath);
        var programMd = Path.Combine(programFolder, "Program.md");

        if (!File.Exists(programMd))
        {
            _logger.LogError("Program.md not found at {ProgramMdPath}", programMd);
            return 1;
        }

        var values = BuildFirmwareValues(settings, configService);

        var jobContext = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PROMPTWARE_DIR"] = programFolder
        };
        if (values.TryGetValue("TendrilPlansFolder", out var plansDir))
            jobContext["PLANS_DIR"] = plansDir;
        if (values.TryGetValue("TendrilPlanFolder", out var planFolder2))
            jobContext["PLAN_DIR"] = planFolder2;

        var resolution = AgentProviderFactory.Resolve(_agentRunner, tendrilSettings, settings.Promptware, settings.Profile, jobContext, settings.Agent);

        var workDir = settings.WorkingDir ?? programFolder;

        string? customInstructions = null;
        if (tendrilSettings.Promptwares.TryGetValue("_default", out var defaultCfg))
            customInstructions = defaultCfg.CustomInstructions;
        if (tendrilSettings.Promptwares.TryGetValue(settings.Promptware, out var specificCfg)
            && !string.IsNullOrWhiteSpace(specificCfg.CustomInstructions))
            customInstructions = specificCfg.CustomInstructions;

        var jobId = JobIdAllocator.AllocateJobId(configService.TendrilHome);
        var logFile = FirmwareCompiler.GetLogFile(programFolder, jobId);
        var firmwareContext = new FirmwareContext(programFolder, values, customInstructions);
        var prompt = FirmwareCompiler.Compile(firmwareContext);

        // Emit resolved context as YAML for testability/debugging
        if (Environment.GetEnvironmentVariable("TENDRIL_QUIET") != "1")
        {
            Console.WriteLine("---");
            Console.WriteLine($"promptware: {settings.Promptware}");
            Console.WriteLine($"promptwarePath: {programFolder}");
            Console.WriteLine($"configPath: {configService.ConfigPath}");
            Console.WriteLine($"tendrilHome: {configService.TendrilHome}");
            Console.WriteLine($"plansDirectory: {configService.PlanFolder}");
            if (values.TryGetValue("TendrilPlanId", out var planId))
                Console.WriteLine($"planId: {planId}");
            if (values.TryGetValue("TendrilPlanFolder", out var planPath))
                Console.WriteLine($"planPath: {planPath}");
            Console.WriteLine($"workingDirectory: {workDir}");
            Console.WriteLine($"logFile: {logFile}");
            Console.WriteLine($"agent: {resolution.AgentId}");
            Console.WriteLine($"model: {resolution.Model}");
            Console.WriteLine($"effort: {resolution.Effort}");
            Console.WriteLine($"profile: {settings.Profile ?? "(from config)"}");
            if (values.Count > 0)
            {
                Console.WriteLine("firmwareValues:");
                foreach (var (key, val) in values.OrderBy(kv => kv.Key))
                    Console.WriteLine($"  {key}: {val}");
            }
            Console.WriteLine("---");
        }

        if (settings.DryRun)
        {
            Console.WriteLine(prompt);
            return 0;
        }

        string? promptFilePath = null;
        if (!resolution.UsesStdinPrompt)
        {
            var tempDir = Path.GetTempPath();
            promptFilePath = Path.Combine(tempDir, $"prompt-{Guid.NewGuid():N}.md");
            File.WriteAllText(promptFilePath, prompt);
        }

        var launchConfig = new AgentLaunchConfig
        {
            Prompt = prompt,
            WorkingDirectory = workDir,
            Model = string.IsNullOrEmpty(resolution.Model) ? null : resolution.Model,
            Effort = AgentProviderFactory.ParseEffort(resolution.Effort),
            PermissionMode = PermissionMode.FullAuto,
            AllowedTools = resolution.AllowedTools,
            ExtraArguments = resolution.ExtraArgs,
            PromptFilePath = promptFilePath,
        };

        var spec = resolution.Cli.BuildProcessSpec(launchConfig);
        var psi = AgentProcessHelper.ToPsi(spec);

        var tendrilHome = configService.TendrilHome;
        if (!string.IsNullOrEmpty(tendrilHome))
            psi.Environment["TENDRIL_HOME"] = tendrilHome;
        psi.Environment["TENDRIL_CONFIG"] = configService.ConfigPath;
        psi.Environment["TENDRIL_PLANS"] = configService.PlanFolder;

        if (!string.IsNullOrEmpty(settings.CliLog))
            psi.Environment["TENDRIL_CLI_LOG"] = settings.CliLog;

        AgentProcessHelper.EnsureTendrilOnPath(psi);
        AgentProcessHelper.ResolveCommandShim(psi);

        _logger.LogInformation("Running {Promptware} via {ProviderName} (model={Model}, effort={Effort})", settings.Promptware, resolution.AgentId, resolution.Model, resolution.Effort);

        var cliCommand = AgentProcessHelper.FormatCliCommand(psi);

        using var process = Process.Start(psi);
        if (process == null)
        {
            _logger.LogError("Failed to start agent process");
            return 1;
        }

        if (resolution.UsesStdinPrompt && psi.RedirectStandardInput)
        {
            process.StandardInput.Write(prompt);
            process.StandardInput.Flush();
            process.StandardInput.Close();
        }

        var outputLines = new List<string>();

        var outputTask = Task.Run(() =>
        {
            while (!process.StandardOutput.EndOfStream)
            {
                var line = process.StandardOutput.ReadLine();
                if (line != null)
                {
                    Console.WriteLine(line);
                    outputLines.Add(line);
                }
            }
        }, cancellationToken);

        var errorTask = Task.Run(() =>
        {
            while (!process.StandardError.EndOfStream)
            {
                var line = process.StandardError.ReadLine();
                if (line != null)
                {
                    Console.Error.WriteLine(line);
                    outputLines.Add($"[stderr] {line}");
                }
            }
        }, cancellationToken);

        process.WaitForExit();
        Task.WaitAll([outputTask, errorTask], TimeSpan.FromSeconds(5));

        try
        {
            var job = new JobItem
            {
                Provider = resolution.AgentId,
                EventParser = _agentRunner.GetParser(resolution.AgentId),
                Status = process.ExitCode == 0 ? JobStatus.Completed : JobStatus.Failed,
                CompletedAt = DateTime.UtcNow,
                ExitCode = process.ExitCode,
                LogFilePath = logFile,
                CompiledPrompt = prompt,
                CliCommand = cliCommand
            };
            foreach (var line in outputLines)
                job.EnqueueOutput(line);

            PromptwareLogWriter.WriteLog(job);
            PromptwareLogWriter.WriteRawLog(logFile, outputLines);
        }
        catch { }

        return process.ExitCode;
    }

    private Dictionary<string, string> BuildFirmwareValues(PromptwareRunSettings settings, ConfigService configService)
    {
        var values = new Dictionary<string, string>();

        // Free-form args joined into TaskDescription header value
        if (settings.Args.Length > 0)
            values["TaskDescription"] = string.Join(" ", settings.Args);

        // --plan resolves plan context
        if (!string.IsNullOrEmpty(settings.Plan))
        {
            try
            {
                var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.Plan);
                values["TendrilPlanFolder"] = planFolder;
                values["TendrilPlansFolder"] = Path.GetDirectoryName(planFolder) ?? "";
                var folderName = Path.GetFileName(planFolder);
                var dashIdx = folderName.IndexOf('-');
                if (dashIdx > 0) values["TendrilPlanId"] = folderName[..dashIdx];
            }
            catch (DirectoryNotFoundException)
            {
                // If it looks like a direct folder path, use it as-is (for testing)
                values["TendrilPlanFolder"] = settings.Plan;
                values["TendrilPlansFolder"] = Path.GetDirectoryName(settings.Plan) ?? "";
            }
        }

        // --value key=value pairs
        if (settings.Values != null)
        {
            foreach (var kv in settings.Values)
            {
                var eqIdx = kv.IndexOf('=');
                if (eqIdx > 0)
                    values[kv[..eqIdx]] = kv[(eqIdx + 1)..];
            }
        }

        return values;
    }

    private static ConfigService CreateConfigFromPath(string configPath)
    {
        var tendrilHome = Path.GetDirectoryName(configPath) ?? "";
        var config = new ConfigService();
        config.SetTendrilHome(tendrilHome);
        return config;
    }
}
