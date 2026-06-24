using System.Diagnostics;
using System.Runtime.InteropServices;
using Ivy.Desktop;
using Ivy.Helpers;
using Ivy.Tendril.Agents;
using Ivy.Tendril.Commands;
using Ivy.Tendril.Database;
using Ivy.Tendril.Infrastructure;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Spectre.Console;
using Spectre.Console.Cli;
using Velopack;

namespace Ivy.Tendril;

public class Program
{
    private const string DetachedLaunchMarker = "--tendril-detached-child";

    // Native console control handler to detect why the process is being killed.
    // This fires BEFORE .NET's ProcessExit and catches CTRL_CLOSE_EVENT which
    // .NET's AppDomain.ProcessExit may not see (Windows force-kills after 5s).
    private delegate bool ConsoleCtrlHandlerDelegate(int ctrlType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandlerDelegate handler, bool add);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);

    [DllImport("libc", SetLastError = true)]
    private static extern int setsid();

    // Must be a static field to prevent GC from collecting the delegate
    private static ConsoleCtrlHandlerDelegate? _consoleCtrlHandler;

    // ConfigService reference for cleanup on exit
    private static ConfigService? _configService;

    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        if (args.Contains(DetachedLaunchMarker))
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    setsid();
                }
            }
            catch { }
        }
        PathHelper.AugmentPath(forceShellPath: false);
        PathHelper.EnsureCliSymlink();

        if (OperatingSystem.IsWindows())
        {
            try
            {
                SetCurrentProcessExplicitAppUserModelID("Ivy Tendril");
            }
            catch { }
        }

        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        VelopackApp.Build().Run();

        var (verbose, quiet, forceDesktop, forceWeb, beta, filteredArgs) = ParseGlobalFlags(args);

        bool isTool = IsTendrilToolInvocation();
        bool isPackagedApp = IsPackagedApp();
        bool useDesktop = (forceDesktop || isPackagedApp || (isTool && !verbose && !quiet)) && !forceWeb;
        if (useDesktop && OperatingSystem.IsLinux())
        {
            // On Linux, default to web mode (foreground server) unless desktop is explicitly forced
            if (!forceDesktop)
            {
                useDesktop = false;
            }
        }



        bool isDetachedChild = args.Contains(DetachedLaunchMarker);

        // Check if we are launching the web server/desktop UI (not executing a CLI subcommand)
        bool isServerLaunch = filteredArgs.Length == 0 || !ShouldHandleAsCliCommand(filteredArgs[0]);
        if (isServerLaunch && !isDetachedChild)
        {
            var checkArgs = new Services.TendrilArgs { Beta = beta, Verbose = verbose, Quiet = quiet };
            var checkServer = TendrilServer.Create(filteredArgs, checkArgs);
            if (useDesktop)
            {
                checkServer.Args.FindAvailablePort = true;
            }
            if (!checkServer.Args.FindAvailablePort && IsPortInUse(checkServer.Args.Port))
            {
                AnsiConsole.MarkupLine($"[red]Error: Port {checkServer.Args.Port} is already in use.[/]");
                AnsiConsole.MarkupLine("[yellow]Please make sure another instance of Tendril is not already running.[/]");
                AnsiConsole.MarkupLine("");
                if (OperatingSystem.IsWindows())
                {
                    AnsiConsole.MarkupLine($"To find the process using this port, run: [blue]netstat -ano | findstr :{checkServer.Args.Port}[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"To find the process using this port, run: [blue]lsof -i :{checkServer.Args.Port}[/]");
                }
                AnsiConsole.MarkupLine("");
                AnsiConsole.MarkupLine("To use a different port, set the [green]PORT[/] environment variable (e.g., [green]PORT=5011 tendril[/]) or specify it directly (e.g., [green]tendril --port 5011[/]).");
                return 1;
            }
        }

        if ((isTool || isPackagedApp) && useDesktop && !isDetachedChild && ShouldDetachDesktopLaunch(filteredArgs, verbose))
            return RelaunchDesktopDetached(filteredArgs);

        if (isDetachedChild && useDesktop)
        {
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
        }

        // Handle CLI commands using Spectre.Console.Cli
        if (filteredArgs.Length > 0)
        {
            var cliServices = new ServiceCollection();
            var cliLogLevel = verbose ? LogLevel.Debug : quiet ? LogLevel.Warning : LogLevel.Information;
            cliServices.AddLogging(builder => builder
                .SetMinimumLevel(cliLogLevel)
                .AddConsole(options => options.FormatterName = "clean")
                .AddConsoleFormatter<CleanConsoleFormatter, ConsoleFormatterOptions>());
            cliServices.AddSingleton<IPlanWatcherService, NullPlanWatcherService>();
            cliServices.AddAgentInfrastructure(opts => opts.IncludeBetaProviders = beta);

            var configService = new ConfigService(Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigService>.Instance);
            cliServices.AddSingleton<IConfigService>(configService);
            cliServices.AddSingleton<ConfigService>(configService);

            var app = ConfigureCliCommands(cliServices);
            var firstArg = filteredArgs[0];

            // Handle --version flag by converting it to "version" command
            if (firstArg == "--version")
                filteredArgs = new[] { "version" };

            if (ShouldHandleAsCliCommand(firstArg))
            {
                try
                {
                    var cliLog = Environment.GetEnvironmentVariable("TENDRIL_CLI_LOG");
                    if (!string.IsNullOrEmpty(cliLog))
                    {
                        var commandLine = string.Join(" ", filteredArgs);
                        var sw = Stopwatch.StartNew();
                        var exitCode = app.Run(filteredArgs);
                        sw.Stop();
                        JobStatusFile.AppendCliInvocationDirect(cliLog, commandLine, exitCode, sw.Elapsed.TotalMilliseconds);
                        return exitCode;
                    }
                    return app.Run(filteredArgs);
                }
                catch (CommandParseException ex)
                {
                    AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message.EscapeMarkup()}");
                    return 1;
                }
                catch (CommandRuntimeException ex)
                {
                    AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message.EscapeMarkup()}");
                    return 1;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                    if (verbose)
                        Console.Error.WriteLine(ex.ToString());
                    return 1;
                }
            }
        }

        // Legacy handlers for commands not yet migrated to Spectre.Console.Cli
        var hashExitCode = HashPasswordCommand.Handle(filteredArgs);
        if (hashExitCode >= 0)
            return hashExitCode;

        var mcpExitCode = McpCommand.Handle(filteredArgs);
        if (mcpExitCode >= 0)
            return mcpExitCode;

        CrashLog.Write($"[{DateTime.UtcNow:O}] Tendril starting (PID {Environment.ProcessId}) | {GetMemoryStats()}");

        // Install native console control handler FIRST — this catches CTRL_CLOSE_EVENT
        // (console window closed), CTRL_C_EVENT, CTRL_BREAK_EVENT, CTRL_LOGOFF_EVENT,
        // and CTRL_SHUTDOWN_EVENT. Logging here tells us exactly WHY the process is dying.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _consoleCtrlHandler = ctrlType =>
            {
                var name = ctrlType switch
                {
                    0 => "CTRL_C_EVENT",
                    1 => "CTRL_BREAK_EVENT",
                    2 => "CTRL_CLOSE_EVENT",
                    5 => "CTRL_LOGOFF_EVENT",
                    6 => "CTRL_SHUTDOWN_EVENT",
                    _ => $"UNKNOWN({ctrlType})"
                };
                CrashLog.Write(
                    $"[{DateTime.UtcNow:O}] ConsoleCtrlHandler: {name} (PID {Environment.ProcessId}) | {GetMemoryStats()}");
                return false; // Let default handling proceed
            };
            SetConsoleCtrlHandler(_consoleCtrlHandler, true);
        }

        ConfigureExceptionHandlers();
        StartMemoryWatchdog();

        var tendrilArgs = new Services.TendrilArgs { Beta = beta, Verbose = verbose, Quiet = quiet };
        var server = TendrilServer.Create(filteredArgs, tendrilArgs);

        if (useDesktop)
        {
            server.Args.FindAvailablePort = true;
        }

        if (!server.Args.FindAvailablePort && IsPortInUse(server.Args.Port))
        {
            AnsiConsole.MarkupLine($"[red]Error: Port {server.Args.Port} is already in use.[/]");
            AnsiConsole.MarkupLine("[yellow]Please make sure another instance of Tendril is not already running.[/]");
            AnsiConsole.MarkupLine("");
            if (OperatingSystem.IsWindows())
            {
                AnsiConsole.MarkupLine($"To find the process using this port, run: [blue]netstat -ano | findstr :{server.Args.Port}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"To find the process using this port, run: [blue]lsof -i :{server.Args.Port}[/]");
            }
            AnsiConsole.MarkupLine("");
            AnsiConsole.MarkupLine("To use a different port, set the [green]PORT[/] environment variable (e.g., [green]PORT=5011 tendril[/]) or specify it directly (e.g., [green]tendril --port 5011[/]).");
            return 1;
        }

        if (useDesktop)
        {
            var iconResource = OperatingSystem.IsWindows() ? "Ivy.Tendril.Assets.icon.ico"
                : OperatingSystem.IsMacOS() ? "Ivy.Tendril.Assets.icon.icns"
                : "Ivy.Tendril.Assets.icon.png";

            var window = new DesktopWindow(server)
                .Title("Ivy Tendril")
                .AppId("Ivy Tendril")
                .Size(1800, 1200)
                .UseDpiScaling(false)
                .Icon(typeof(Program), iconResource)
                .OnReady(w =>
                {
                    if (server.ServiceProvider is { } sp)
                    {
                        var statusService = sp.GetService<ITendrilProcessStatusService>();
                        if (statusService != null)
                        {
                            UpdateBadge(w, statusService.Current.JobCount);
                            statusService.Status.Subscribe(s => UpdateBadge(w, s.JobCount));
                        }

                        var jobService = sp.GetService<IJobService>();
                        var configService = sp.GetService<IConfigService>();
                        if (jobService != null)
                        {
                            jobService.NotificationReady += notification =>
                            {
                                if (configService?.Settings.DesktopNotifications != false)
                                {
                                    DesktopWindow.ShowNotification(
                                        notification.Title,
                                        notification.Message,
                                        appId: "Ivy Tendril");
                                }
                            };
                        }
                    }
                });

            return window.Run();
        }
        else
        {
            await server.RunAsync();
            return 0;
        }
    }

    private static (bool verbose, bool quiet, bool forceDesktop, bool forceWeb, bool beta, string[] filtered)
        ParseGlobalFlags(string[] args)
    {
        bool verbose = args.Contains("--verbose") || args.Contains("-v");
        bool quiet = args.Contains("--quiet") || args.Contains("-q");
        bool forceDesktop = args.Contains("--desktop");
        bool forceWeb = args.Contains("--web");
        bool beta = args.Contains("--beta");

        if (verbose)
            Environment.SetEnvironmentVariable("TENDRIL_VERBOSE", "1");
        if (quiet)
            Environment.SetEnvironmentVariable("TENDRIL_QUIET", "1");
        if (beta)
            Environment.SetEnvironmentVariable("TENDRIL_BETA", "1");

        var filtered = args.Where(a =>
            a != "--desktop" && a != "--web" &&
            a != "--verbose" && a != "-v" &&
            a != "--quiet" && a != "-q" &&
            a != "--beta" &&
            a != DetachedLaunchMarker).ToArray();

        return (verbose, quiet, forceDesktop, forceWeb, beta, filtered);
    }

    private static bool ShouldHandleAsCliCommand(string firstArg)
    {
        string[] cliCommands = new[]
        {
            "doctor", "db-version", "db-migrate", "db-reset",
            "update-promptwares", "job", "plan", "promptware",
            "trash", "verification", "project", "models",
            "version", "--version", "report-bug", "reset", "update",
            "--help", "-h", "run"
        };
        return cliCommands.Contains(firstArg);
    }

    private static bool IsPackagedApp()
    {
        return Velopack.Locators.VelopackLocator.Current?.CurrentlyInstalledVersion != null;
    }

    private static bool ShouldDetachDesktopLaunch(string[] filteredArgs, bool verbose)
    {
        if (IsPackagedApp())
            return false;

        // Detach only for desktop startup, not for CLI commands, and NOT if verbose logging is requested.
        return filteredArgs.Length == 0 && !verbose;
    }

    private static bool IsTendrilToolInvocation()
    {
        // If the executing assembly is in the .store / .dotnet folder, it's a global tool invocation
        var path = System.AppContext.BaseDirectory;
        if (path.Contains(".store", StringComparison.OrdinalIgnoreCase) ||
            path.Contains(".dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // ProcessPath can be "dotnet" for global tools, so inspect argv[0] too.
        var processPathName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
        if (processPathName.Equals("tendril", StringComparison.OrdinalIgnoreCase))
            return true;

        var argv0 = Environment.GetCommandLineArgs().FirstOrDefault() ?? string.Empty;
        var argv0Name = Path.GetFileNameWithoutExtension(argv0);
        return argv0Name.Equals("tendril", StringComparison.OrdinalIgnoreCase);
    }

    private static int RelaunchDesktopDetached(string[] filteredArgs)
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            Console.Error.WriteLine("Unable to determine tendril executable path.");
            return 1;
        }

        var childArgs = new List<string>(filteredArgs)
        {
            "--desktop",
            DetachedLaunchMarker
        };

        try
        {
            if (OperatingSystem.IsWindows())
            {
                var startInfo = new ProcessStartInfo(processPath)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (var arg in childArgs)
                    startInfo.ArgumentList.Add(arg);
                Process.Start(startInfo);
            }
            else
            {
                // On macOS/Linux, run via shell with nohup and redirect streams to /dev/null
                // to completely detach the shim wrapper and grandchild processes from the TTY.
                var escapedPath = processPath.Replace("\"", "\\\"");
                var escapedArgs = string.Join(" ", childArgs.Select(a => $"\"{a.Replace("\"", "\\\"")}\""));
                var shellCmd = $"nohup \"{escapedPath}\" {escapedArgs} >/dev/null 2>&1 &";

                var startInfo = new ProcessStartInfo("/bin/sh")
                {
                    ArgumentList = { "-c", shellCmd },
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                Process.Start(startInfo);
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to launch desktop mode: {ex.Message}");
            return 1;
        }
    }

    private static CommandApp ConfigureCliCommands(ServiceCollection cliServices)
    {
        var registrar = new TypeRegistrar(cliServices);
        var app = new CommandApp(registrar);
        app.Configure(config =>
        {
            config.PropagateExceptions();

            // Doctor command
            config.AddCommand<DoctorCliCommand>("doctor")
                .WithDescription("System health check");

            // Run command
            config.AddCommand<RunCommand>("run")
                .WithDescription("Run the Tendril web server in the foreground");

            // Database commands
            config.AddCommand<DbVersionCommand>("db-version")
                .WithDescription("Show database version");
            config.AddCommand<DbMigrateCommand>("db-migrate")
                .WithDescription("Apply database migrations");
            config.AddCommand<DbResetCommand>("db-reset")
                .WithDescription("Reset database");

            config.AddCommand<ResetCommand>("reset")
                .WithDescription("Remove all Tendril data and environment variables");

            // Other commands
            config.AddCommand<UpdatePromptwaresCliCommand>("update-promptwares")
                .WithDescription("Update embedded promptwares");
            config.AddBranch("promptware", pw =>
            {
                pw.AddCommand<PromptwareRunCommand>("run")
                    .WithDescription("Run a promptware directly");
                pw.AddCommand<PromptwareReadMemoryCommand>("read-memory")
                    .WithDescription("Read a promptware memory file to STDOUT");
                pw.AddCommand<PromptwareWriteMemoryCommand>("write-memory")
                    .WithDescription("Write a promptware memory file from STDIN");
                pw.AddCommand<PromptwareWriteToolCommand>("write-tool")
                    .WithDescription("Write a promptware tool file from STDIN");
            });
            config.AddCommand<VersionCommand>("version")
                .WithDescription("Show version information");
            config.AddCommand<UpdateCliCommand>("update")
                .WithDescription("Update Tendril to the latest version");
            config.AddCommand<ReportBugCommand>("report-bug")
                .WithDescription("Report a bug with plan/job context");

            // Job management commands
            config.AddBranch("job", job =>
            {
                job.AddCommand<JobStatusCommand>("status")
                    .WithDescription("Report job status (message, planId, planTitle)");
                job.AddCommand<JobStartCommand>("start")
                    .WithDescription("Start a job via the running Tendril server");
            });

            // Plan management commands
            config.AddBranch("plan", plan =>
            {
                plan.AddCommand<PlanListCommand>("list")
                    .WithDescription("List plans with optional filters");
                plan.AddCommand<PlanCreateCommand>("create")
                    .WithDescription("Create a new plan");
                plan.AddCommand<PlanUpdateCommand>("update")
                    .WithDescription("Update plan from STDIN");
                plan.AddCommand<PlanSetCommand>("set")
                    .WithDescription("Set a single field");
                plan.AddCommand<PlanAddRepoCommand>("add-repo")
                    .WithDescription("Add a repository");
                plan.AddCommand<PlanRemoveRepoCommand>("remove-repo")
                    .WithDescription("Remove a repository");
                plan.AddCommand<PlanAddPrCommand>("add-pr")
                    .WithDescription("Add a PR URL");
                plan.AddCommand<PlanAddCommitCommand>("add-commit")
                    .WithDescription("Add a commit hash");
                plan.AddCommand<PlanAddRelatedPlanCommand>("add-related-plan")
                    .WithDescription("Add a related plan");
                plan.AddCommand<PlanRemoveRelatedPlanCommand>("remove-related-plan")
                    .WithDescription("Remove a related plan");
                plan.AddCommand<PlanAddDependsOnCommand>("add-depends-on")
                    .WithDescription("Add a plan dependency");
                plan.AddCommand<PlanRemoveDependsOnCommand>("remove-depends-on")
                    .WithDescription("Remove a plan dependency");
                plan.AddCommand<PlanSetVerificationCommand>("set-verification")
                    .WithDescription("Update verification status");
                plan.AddCommand<PlanGetCommand>("get")
                    .WithDescription("Read plan or field");
                plan.AddCommand<PlanAddLogCommand>("add-log")
                    .WithDescription("Write a log entry");
                plan.AddCommand<PlanWriteRevisionCommand>("write-revision")
                    .WithDescription("Write a revision file from STDIN");
                plan.AddCommand<PlanValidateCommand>("validate")
                    .WithDescription("Validate plan health");
                plan.AddCommand<PlanCleanupCommand>("cleanup")
                    .WithDescription("Remove worktrees from a plan");
                plan.AddCommand<PlanRemoveWorktreeCommand>("remove-worktree")
                    .WithDescription("Remove a single worktree from a plan");
                plan.AddCommand<PlanDoctorCommand>("doctor")
                    .WithDescription("Check plan health");

                plan.AddBranch("rec", rec =>
                {
                    rec.AddCommand<PlanRecListCommand>("list")
                        .WithDescription("List recommendations");
                    rec.AddCommand<PlanRecAddCommand>("add")
                        .WithDescription("Add a recommendation");
                    rec.AddCommand<PlanRecRemoveCommand>("remove")
                        .WithDescription("Remove a recommendation");
                    rec.AddCommand<PlanRecSetCommand>("set")
                        .WithDescription("Update a recommendation field");
                    rec.AddCommand<PlanRecAcceptCommand>("accept")
                        .WithDescription("Accept a recommendation");
                    rec.AddCommand<PlanRecDeclineCommand>("decline")
                        .WithDescription("Decline a recommendation");
                });

                plan.AddBranch("verification", verification =>
                {
                    verification.AddCommand<PlanVerificationListCommand>("list")
                        .WithDescription("List verifications on a plan");
                    verification.AddCommand<PlanVerificationAddCommand>("add")
                        .WithDescription("Add a verification to a plan");
                    verification.AddCommand<PlanVerificationRemoveCommand>("remove")
                        .WithDescription("Remove a verification from a plan");
                });
            });

            config.AddBranch("verification", verification =>
            {
                verification.AddCommand<VerificationListCommand>("list")
                    .WithDescription("List global verification definitions");
                verification.AddCommand<VerificationGetCommand>("get")
                    .WithDescription("Get the full prompt for a verification definition");
                verification.AddCommand<VerificationAddCommand>("add")
                    .WithDescription("Add a verification definition");
                verification.AddCommand<VerificationRemoveCommand>("remove")
                    .WithDescription("Remove a verification definition");
                verification.AddCommand<VerificationSetCommand>("set")
                    .WithDescription("Update a verification definition field");
            });

            config.AddCommand<ModelsCommand>("models")
                .WithDescription("List available models and pricing for agent CLIs");

            config.AddCommand<AgentInstructionsCommand>("agent-instructions")
                .WithDescription("Print the agent system prompt");

            config.AddBranch("trash", trash =>
            {
                trash.AddCommand<TrashWriteCommand>("write")
                    .WithDescription("Write a file to Trash from STDIN");
            });

            config.AddBranch("project", project =>
            {
                project.AddCommand<ProjectListCommand>("list")
                    .WithDescription("List all projects");
                project.AddCommand<ProjectGetCommand>("get")
                    .WithDescription("Show details of a project");
                project.AddCommand<ProjectAddCommand>("add")
                    .WithDescription("Add a project");
                project.AddCommand<ProjectRemoveCommand>("remove")
                    .WithDescription("Remove a project");
                project.AddCommand<ProjectSetCommand>("set")
                    .WithDescription("Set a project field");
                project.AddCommand<ProjectAddRepoCommand>("add-repo")
                    .WithDescription("Add a repository to a project");
                project.AddCommand<ProjectRemoveRepoCommand>("remove-repo")
                    .WithDescription("Remove a repository from a project");
                project.AddCommand<ProjectAddVerificationCommand>("add-verification")
                    .WithDescription("Add a verification to a project");
                project.AddCommand<ProjectRemoveVerificationCommand>("remove-verification")
                    .WithDescription("Remove a verification from a project");
                project.AddCommand<ProjectMoveVerificationCommand>("move-verification")
                    .WithDescription("Move a verification to a different position in the list");
                project.AddCommand<ProjectAddBuildDepCommand>("add-build-dep")
                    .WithDescription("Add a build dependency to a project");
                project.AddCommand<ProjectRemoveBuildDepCommand>("remove-build-dep")
                    .WithDescription("Remove a build dependency from a project");
                project.AddCommand<ProjectAddReviewActionCommand>("add-review-action")
                    .WithDescription("Add a review action to a project");
                project.AddCommand<ProjectRemoveReviewActionCommand>("remove-review-action")
                    .WithDescription("Remove a review action from a project");
            });
        });
        return app;
    }

    private static void ConfigureExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var msg = $"[{DateTime.UtcNow:O}] FATAL UnhandledException (IsTerminating={e.IsTerminating}) | {GetMemoryStats()}\n  {e.ExceptionObject}";
            Console.WriteLine($"[FATAL] Unhandled exception: {e.ExceptionObject}");
            CrashLog.Write(msg);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            var msg = $"[{DateTime.UtcNow:O}] FATAL UnobservedTaskException | {GetMemoryStats()}\n  {e.Exception}";
            Console.WriteLine($"[FATAL] Unobserved task exception: {e.Exception}");
            CrashLog.Write(msg);
            e.SetObserved();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            CrashLog.Write($"[{DateTime.UtcNow:O}] ProcessExit event fired (PID {Environment.ProcessId}) | {GetMemoryStats()}");

            // Clean up .master file if we own it
            try
            {
                var home = Environment.GetEnvironmentVariable("TENDRIL_HOME")?.Trim();
                if (!string.IsNullOrEmpty(home))
                {
                    var masterFile = Path.Combine(home, ".master");
                    if (File.Exists(masterFile))
                    {
                        var masterJson = File.ReadAllText(masterFile);
                        var masterData = System.Text.Json.JsonSerializer.Deserialize<Services.MasterElectionService.MasterFileData>(
                            masterJson, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                        if (masterData?.Pid == Environment.ProcessId)
                            File.Delete(masterFile);
                    }
                }
            }
            catch { }

            // Clean up tracked temp files
            try
            {
                (_configService as IDisposable)?.Dispose();
            }
            catch (Exception ex)
            {
                CrashLog.Write($"[{DateTime.UtcNow:O}] Failed to dispose ConfigService: {ex}");
            }
        };
    }

    private static void StartMemoryWatchdog()
    {
        _ = Task.Run(async () =>
        {
            const long warningThresholdBytes = 1L * 1024 * 1024 * 1024; // 1 GB
            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(5));
                try
                {
                    using var proc = Process.GetCurrentProcess();
                    if (proc.WorkingSet64 > warningThresholdBytes)
                        CrashLog.Write($"[{DateTime.UtcNow:O}] MEMORY WARNING | {GetMemoryStats()}");
                }
                catch { /* best-effort */ }
            }
        });
    }

    internal static void SetConfigServiceForCleanup(ConfigService configService)
    {
        _configService = configService;
    }

    private static string GetMemoryStats()
    {
        try
        {
            using var proc = Process.GetCurrentProcess();
            var workingSet = proc.WorkingSet64;
            var gcHeap = GC.GetTotalMemory(false);
            return $"WorkingSet={workingSet / (1024 * 1024)}MB, GCHeap={gcHeap / (1024 * 1024)}MB, Gen0={GC.CollectionCount(0)}, Gen1={GC.CollectionCount(1)}, Gen2={GC.CollectionCount(2)}";
        }
        catch
        {
            return "Memory stats unavailable";
        }
    }

    private static void UpdateBadge(DesktopWindow window, int activeJobs)
    {
        if (activeJobs > 0)
            window.SetBadgeCount(activeJobs, background: "#5B21B6", foreground: "#FFFFFF");
        else
            window.ClearBadge();
    }

    private static bool IsPortInUse(int port)
    {
        try
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return false;
        }
        catch
        {
            return true;
        }
    }
}
