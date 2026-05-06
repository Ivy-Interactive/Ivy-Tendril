using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services.Agents;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PromptwareRunSettings : CommandSettings
{
    [Description("Promptware name (e.g., IvyFrameworkVerification)")]
    [CommandArgument(0, "<promptware>")]
    public string Promptware { get; set; } = "";

    [Description("Remaining arguments passed to the promptware as firmware values")]
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

    public override Spectre.Console.ValidationResult Validate()
    {
        if (Args.Length > 0 && !Directory.Exists(Args[0]) && !File.Exists(Args[0]))
            return Spectre.Console.ValidationResult.Error($"First argument '{Args[0]}' is not a valid path.");

        return Spectre.Console.ValidationResult.Success();
    }
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

    /// <summary>
    ///     Resolves the promptware folder by checking the source root first,
    ///     then falling back to TENDRIL_HOME/Promptwares/ for promptwares that
    ///     only exist in the deployed location (e.g. team config promptwares).
    /// </summary>
    private static string ResolvePromptwareFolder(string promptwareName, string? tendrilHome)
    {
        var sourceRoot = Ivy.Tendril.Helpers.PromptwareHelper.ResolvePromptsRoot(tendrilHome);
        var sourceFolder = Path.Combine(sourceRoot, promptwareName);

        if (File.Exists(Path.Combine(sourceFolder, "Program.md")))
            return sourceFolder;

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
        var configService = new ConfigService();
        var tendrilSettings = configService.Settings;

        var programFolder = ResolvePromptwareFolder(settings.Promptware, configService.TendrilHome);
        var programMd = Path.Combine(programFolder, "Program.md");

        if (!File.Exists(programMd))
        {
            _logger.LogError("Program.md not found at {ProgramMdPath}", programMd);
            return 1;
        }

        // Build firmware values from args
        var values = new Dictionary<string, string>();
        if (settings.Args.Length > 0)
        {
            values["Args"] = settings.Args[0];

            // If first arg looks like a plan folder, populate plan-related values
            var firstArg = settings.Args[0];
            if (Directory.Exists(firstArg))
            {
                values["PlanFolder"] = firstArg;
                values["PlansDirectory"] = Path.GetDirectoryName(firstArg) ?? "";
                var folderName = Path.GetFileName(firstArg);
                var dashIdx = folderName.IndexOf('-');
                if (dashIdx > 0) values["PlanId"] = folderName[..dashIdx];
            }
        }

        // Parse --value key=value pairs
        if (settings.Values != null)
        {
            foreach (var kv in settings.Values)
            {
                var eqIdx = kv.IndexOf('=');
                if (eqIdx > 0)
                    values[kv[..eqIdx]] = kv[(eqIdx + 1)..];
            }
        }

        // Resolve agent provider and profile
        var resolution = AgentProviderFactory.Resolve(tendrilSettings, settings.Promptware, settings.Profile);

        // Determine working directory
        var workDir = settings.WorkingDir ?? programFolder;

        // Compile firmware
        var logFile = FirmwareCompiler.GetNextLogFile(programFolder, values);
        var firmwareContext = new FirmwareContext(programFolder, logFile, values);
        var prompt = FirmwareCompiler.Compile(firmwareContext);

        // Build invocation
        var invocation = new AgentInvocation(
            PromptContent: prompt,
            WorkingDirectory: workDir,
            Model: resolution.Model,
            Effort: resolution.Effort,
            SessionId: "",
            AllowedTools: resolution.AllowedTools,
            ExtraArgs: resolution.ExtraArgs);

        var psi = resolution.Provider.BuildProcessStart(invocation);

        // Set environment so child processes (including tendril CLI calls from the agent)
        // resolve the same home/config/plans directories as this process.
        var tendrilHome = configService.TendrilHome;
        if (!string.IsNullOrEmpty(tendrilHome))
            psi.Environment["TENDRIL_HOME"] = tendrilHome;
        psi.Environment["TENDRIL_CONFIG"] = configService.ConfigPath;
        psi.Environment["TENDRIL_PLANS"] = configService.PlanFolder;

        JobLauncher.EnsureTendrilOnPath(psi);

        var verbosityService = new VerbosityService();
        if (verbosityService.Level != VerbosityLevel.Quiet)
        {
            _logger.LogInformation("Running {Promptware} via {ProviderName} (model={Model}, effort={Effort})", settings.Promptware, resolution.Provider.Name, resolution.Model, resolution.Effort);
        }

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

        // Stream stdout to our stdout
        var outputTask = Task.Run(() =>
        {
            while (!process.StandardOutput.EndOfStream)
            {
                var line = process.StandardOutput.ReadLine();
                if (line != null) Console.WriteLine(line);
            }
        }, cancellationToken);

        // Stream stderr to our stderr
        var errorTask = Task.Run(() =>
        {
            while (!process.StandardError.EndOfStream)
            {
                var line = process.StandardError.ReadLine();
                // Passthrough stderr from child process — not logged to avoid polluting parent logs
                if (line != null) Console.Error.WriteLine(line);
            }
        }, cancellationToken);

        process.WaitForExit();
        Task.WaitAll([outputTask, errorTask], TimeSpan.FromSeconds(5));

        return process.ExitCode;
    }
}
