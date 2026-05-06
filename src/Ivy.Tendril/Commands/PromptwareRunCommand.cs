using System.ComponentModel;
using System.Diagnostics;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services.Agents;
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
    [Description("Plan ID or folder path — populates PlanFolder, PlansDirectory, PlanId")]
    public string? Plan { get; init; }

    [CommandOption("--promptware-path")]
    [Description("Additional directory to search for promptware folders")]
    public string? PromptwarePath { get; init; }

    [CommandOption("--config")]
    [Description("Override config.yaml path (for testing)")]
    public string? ConfigPath { get; init; }

    [CommandOption("--agent-cmd")]
    [Description("Override agent CLI command (for testing, e.g. 'echo' to dry-run)")]
    public string? AgentCmd { get; init; }

    [CommandOption("--dry-run")]
    [Description("Print the compiled firmware and exit without launching the agent")]
    public bool DryRun { get; init; }
}

public class PromptwareRunCommand : Command<PromptwareRunSettings>
{
    private readonly ILogger<PromptwareRunCommand> _logger;

    public PromptwareRunCommand(ILogger<PromptwareRunCommand> logger) => _logger = logger;

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

    private static string ResolvePromptwareFolder(string promptwareName, string? tendrilHome, string? promptwarePath)
    {
        // 1. --promptware-path override (highest priority)
        if (!string.IsNullOrEmpty(promptwarePath))
        {
            var overrideFolder = Path.Combine(promptwarePath, promptwareName);
            if (File.Exists(Path.Combine(overrideFolder, "Program.md")))
                return overrideFolder;
        }

        // 2. Source/debug mode (AppContext.BaseDirectory relative)
        var sourceRoot = PromptwareHelper.ResolvePromptsRoot(tendrilHome);
        var sourceFolder = Path.Combine(sourceRoot, promptwareName);

        if (File.Exists(Path.Combine(sourceFolder, "Program.md")))
            return sourceFolder;

        // 3. TENDRIL_HOME/Promptwares (deployed mode)
        tendrilHome ??= Environment.GetEnvironmentVariable("TENDRIL_HOME");
        if (!string.IsNullOrEmpty(tendrilHome))
        {
            var deployedRoot = Path.Combine(tendrilHome, "Promptwares");
            var deployedFolder = Path.Combine(deployedRoot, promptwareName);
            if (File.Exists(Path.Combine(deployedFolder, "Program.md")))
                return deployedFolder;
        }

        return sourceFolder;
    }

    internal int Run(PromptwareRunSettings settings, CancellationToken cancellationToken = default)
    {
        var configService = !string.IsNullOrEmpty(settings.ConfigPath)
            ? CreateConfigFromPath(settings.ConfigPath)
            : new ConfigService();

        var tendrilSettings = configService.Settings;

        var programFolder = ResolvePromptwareFolder(settings.Promptware, configService.TendrilHome, settings.PromptwarePath);
        var programMd = Path.Combine(programFolder, "Program.md");

        if (!File.Exists(programMd))
        {
            _logger.LogError("Program.md not found at {ProgramMdPath}", programMd);
            return 1;
        }

        var values = BuildFirmwareValues(settings, configService);

        var resolution = AgentProviderFactory.Resolve(tendrilSettings, settings.Promptware, settings.Profile);

        var workDir = settings.WorkingDir ?? programFolder;

        var logFile = FirmwareCompiler.GetNextLogFile(programFolder, values);
        var firmwareContext = new FirmwareContext(programFolder, logFile, values);
        var prompt = FirmwareCompiler.Compile(firmwareContext);

        // Emit resolved context as YAML for testability/debugging
        var verbosityService = new VerbosityService();
        if (verbosityService.Level != VerbosityLevel.Quiet)
        {
            Console.WriteLine("---");
            Console.WriteLine($"promptware: {settings.Promptware}");
            Console.WriteLine($"promptwarePath: {programFolder}");
            Console.WriteLine($"configPath: {configService.ConfigPath}");
            Console.WriteLine($"tendrilHome: {configService.TendrilHome}");
            Console.WriteLine($"plansDirectory: {configService.PlanFolder}");
            if (values.TryGetValue("PlanId", out var planId))
                Console.WriteLine($"planId: {planId}");
            if (values.TryGetValue("PlanFolder", out var planPath))
                Console.WriteLine($"planPath: {planPath}");
            Console.WriteLine($"workingDirectory: {workDir}");
            Console.WriteLine($"logFile: {logFile}");
            Console.WriteLine($"agent: {resolution.Provider.Name}");
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

        var invocation = new AgentInvocation(
            PromptContent: prompt,
            WorkingDirectory: workDir,
            Model: resolution.Model,
            Effort: resolution.Effort,
            SessionId: "",
            AllowedTools: resolution.AllowedTools,
            ExtraArgs: resolution.ExtraArgs);

        var psi = resolution.Provider.BuildProcessStart(invocation);

        if (!string.IsNullOrEmpty(settings.AgentCmd))
            psi.FileName = settings.AgentCmd;

        var tendrilHome = configService.TendrilHome;
        if (!string.IsNullOrEmpty(tendrilHome))
            psi.Environment["TENDRIL_HOME"] = tendrilHome;
        psi.Environment["TENDRIL_CONFIG"] = configService.ConfigPath;
        psi.Environment["TENDRIL_PLANS"] = configService.PlanFolder;

        JobLauncher.EnsureTendrilOnPath(psi);

        if (verbosityService.Level != VerbosityLevel.Quiet)
            _logger.LogInformation("Running {Promptware} via {ProviderName} (model={Model}, effort={Effort})", settings.Promptware, resolution.Provider.Name, resolution.Model, resolution.Effort);

        using var process = Process.Start(psi);
        if (process == null)
        {
            _logger.LogError("Failed to start agent process");
            return 1;
        }

        if (resolution.Provider.UsesStdinPrompt && psi.RedirectStandardInput)
        {
            process.StandardInput.Write(prompt);
            process.StandardInput.Flush();
            process.StandardInput.Close();
        }

        var outputTask = Task.Run(() =>
        {
            while (!process.StandardOutput.EndOfStream)
            {
                var line = process.StandardOutput.ReadLine();
                if (line != null) Console.WriteLine(line);
            }
        }, cancellationToken);

        var errorTask = Task.Run(() =>
        {
            while (!process.StandardError.EndOfStream)
            {
                var line = process.StandardError.ReadLine();
                if (line != null) Console.Error.WriteLine(line);
            }
        }, cancellationToken);

        process.WaitForExit();
        Task.WaitAll([outputTask, errorTask], TimeSpan.FromSeconds(5));

        return process.ExitCode;
    }

    private Dictionary<string, string> BuildFirmwareValues(PromptwareRunSettings settings, ConfigService configService)
    {
        var values = new Dictionary<string, string>();

        // Free-form args joined into a single Args value
        if (settings.Args.Length > 0)
            values["Args"] = string.Join(" ", settings.Args);

        // --plan resolves plan context
        if (!string.IsNullOrEmpty(settings.Plan))
        {
            try
            {
                var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.Plan);
                values["PlanFolder"] = planFolder;
                values["PlansDirectory"] = Path.GetDirectoryName(planFolder) ?? "";
                var folderName = Path.GetFileName(planFolder);
                var dashIdx = folderName.IndexOf('-');
                if (dashIdx > 0) values["PlanId"] = folderName[..dashIdx];
            }
            catch (DirectoryNotFoundException)
            {
                // If it looks like a direct folder path, use it as-is (for testing)
                values["PlanFolder"] = settings.Plan;
                values["PlansDirectory"] = Path.GetDirectoryName(settings.Plan) ?? "";
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
