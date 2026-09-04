using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Ivy.Desktop;
using Ivy.Helpers;
using Ivy.Tendril.Agents;
using Ivy.Tendril.Commands;
using Ivy.Tendril.Database;
using Ivy.Tendril.Infrastructure;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Git;
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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetStdHandle(int nStdHandle, IntPtr hHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFileType(IntPtr hFile);

    private const int ATTACH_PARENT_PROCESS = -1;
    private const int STD_INPUT_HANDLE = -10;
    private const int STD_OUTPUT_HANDLE = -11;
    private const int STD_ERROR_HANDLE = -12;
    private const uint FILE_TYPE_DISK = 0x0001;
    private const uint FILE_TYPE_PIPE = 0x0003;

    // A console handle is FILE_TYPE_CHAR; a shell redirect (`> file`, `| other`) yields
    // FILE_TYPE_DISK or FILE_TYPE_PIPE. FILE_TYPE_UNKNOWN (invalid handle) is not redirected.
    internal static bool IsHandleRedirected(IntPtr handle)
    {
        var type = GetFileType(handle);
        return type == FILE_TYPE_DISK || type == FILE_TYPE_PIPE;
    }

    // Must be a static field to prevent GC from collecting the delegate
    private static ConsoleCtrlHandlerDelegate? _consoleCtrlHandler;

    // ConfigService reference for cleanup on exit
    private static ConfigService? _configService;

    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        try
        {
            VelopackApp.Build().Run();
        }
        catch { }

        var (verbose, quiet, forceDesktop, forceWeb, beta, filteredArgs) = ParseGlobalFlags(args);

        bool isTool = IsTendrilToolInvocation();
        bool isPackagedApp = IsPackagedApp();
        bool useDesktop = (forceDesktop || isPackagedApp || (isTool && !verbose && !quiet) || (!isTool && !isPackagedApp && !forceWeb)) && !forceWeb;
        if (useDesktop && OperatingSystem.IsLinux())
        {
            // On Linux, default to web mode (foreground server) unless desktop is explicitly forced
            if (!forceDesktop)
            {
                useDesktop = false;
            }
        }

        var invocationKind = CliDispatcher.Classify(filteredArgs);

        if (OperatingSystem.IsWindows())
        {
            // Snapshot the inherited std handles BEFORE attaching. AttachConsole overwrites all three
            // with console handles, which silently discards `tendril ... > file`, `tendril ... | other`
            // and `tendril ... --stdin < file` for GUI-subsystem builds (issue #1849).
            var inheritedIn = GetStdHandle(STD_INPUT_HANDLE);
            var inheritedOut = GetStdHandle(STD_OUTPUT_HANDLE);
            var inheritedErr = GetStdHandle(STD_ERROR_HANDLE);
            var inRedirected = IsHandleRedirected(inheritedIn);
            var outRedirected = IsHandleRedirected(inheritedOut);
            var errRedirected = IsHandleRedirected(inheritedErr);

            if (AttachConsole(ATTACH_PARENT_PROCESS))
            {
                // Put the redirected targets back; leave the newly attached console handles in place
                // for the streams the parent did NOT redirect.
                if (inRedirected) SetStdHandle(STD_INPUT_HANDLE, inheritedIn);
                if (outRedirected) SetStdHandle(STD_OUTPUT_HANDLE, inheritedOut);
                if (errRedirected) SetStdHandle(STD_ERROR_HANDLE, inheritedErr);

                try
                {
                    var stdout = Console.OpenStandardOutput();
                    Console.SetOut(new StreamWriter(stdout, new UTF8Encoding(false)) { AutoFlush = true });
                    var stderr = Console.OpenStandardError();
                    Console.SetError(new StreamWriter(stderr, new UTF8Encoding(false)) { AutoFlush = true });
                }
                catch { }
            }
        }

        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        if (!Console.IsInputRedirected)
        {
            try
            {
                Console.InputEncoding = utf8NoBom;
            }
            catch { }
        }

        try
        {
            Console.OutputEncoding = utf8NoBom;
        }
        catch { }

        try
        {
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Out) });
        }
        catch { }

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

        var legacyRedirectExitCode = TryRedirectLegacyToolInvocation(args);
        if (legacyRedirectExitCode.HasValue)
            return legacyRedirectExitCode.Value;

        PathHelper.EnsureCliSymlink();

        if (OperatingSystem.IsWindows())
        {
            try
            {
                SetCurrentProcessExplicitAppUserModelID("Ivy Tendril");
            }
            catch { }
        }

        bool isDetachedChild = args.Contains(DetachedLaunchMarker);

        if (invocationKind == CliInvocationKind.Help)
        {
            var helpApp = ConfigureCliCommands(new ServiceCollection());
            try
            {
                return helpApp.Run(filteredArgs.Length == 0 ? new[] { "--help" } : filteredArgs);
            }
            catch (CommandParseException)
            {
                // First token wasn't a registered command (e.g. "add project --help") but a
                // help token is present somewhere in the args — fall back to top-level help
                // rather than letting Spectre's parse failure escape as an "unknown command" error.
                return helpApp.Run(new[] { "--help" });
            }
        }

        if (invocationKind == CliInvocationKind.Unknown)
        {
            AnsiConsole.MarkupLine($"[red]Unknown command '{filteredArgs[0].EscapeMarkup()}'.[/] Run [green]tendril --help[/] to see available commands.");
            ConfigureCliCommands(new ServiceCollection()).Run(new[] { "--help" });
            return 1;
        }

        if (invocationKind == CliInvocationKind.LegacyCliCommand)
        {
            var hashExitCode = HashPasswordCommand.Handle(filteredArgs);
            if (hashExitCode >= 0)
                return hashExitCode;

            var mcpExitCode = McpCommand.Handle(filteredArgs);
            if (mcpExitCode >= 0)
                return mcpExitCode;
        }

        // Check if we are launching the web server/desktop UI (not executing a CLI subcommand)
        bool isServerLaunch = invocationKind == CliInvocationKind.ServerLaunch;
        if (isServerLaunch && !isDetachedChild)
        {
            CrashLog.Write($"[{DateTime.UtcNow:O}] Server launch (kind={invocationKind}) | raw args: {string.Join(" ", Environment.GetCommandLineArgs())}");
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
        if (invocationKind == CliInvocationKind.CliCommand || invocationKind == CliInvocationKind.Version)
        {
            var cliServices = new ServiceCollection();
            var cliLogLevel = verbose ? LogLevel.Debug : quiet ? LogLevel.Warning : LogLevel.Information;
            cliServices.AddLogging(builder => builder
                .SetMinimumLevel(cliLogLevel)
                .AddConsole(options => options.FormatterName = "clean")
                .AddConsoleFormatter<CleanConsoleFormatter, ConsoleFormatterOptions>());
            cliServices.AddSingleton<IPlanWatcherService, NullPlanWatcherService>();
            var configService = new ConfigService(Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigService>.Instance);
            cliServices.AddSingleton<IConfigService>(configService);
            cliServices.AddSingleton<ConfigService>(configService);
            cliServices.AddAgentInfrastructure(opts => opts.IncludeBetaProviders = beta || configService.Settings.Beta || Environment.GetEnvironmentVariable("TENDRIL_BETA") == "1" || Environment.GetEnvironmentVariable("IVY_BETA") == "1");

            // Needed by `plan create` to validate that a source issue/PR URL belongs to the
            // chosen project (PlanSourceProjectGuard). Resolves git remotes of project repos.
            cliServices.AddSingleton<GithubService>();
            cliServices.AddSingleton<IGithubService>(sp => sp.GetRequiredService<GithubService>());

            var app = ConfigureCliCommands(cliServices);

            // Handle --version flag by converting it to "version" command
            if (invocationKind == CliInvocationKind.Version)
                filteredArgs = new[] { "version" };

            try
            {
                var cliLog = Environment.GetEnvironmentVariable("TENDRIL_CLI_LOG");
                if (!string.IsNullOrEmpty(cliLog))
                {
                    var commandLine = string.Join(" ", filteredArgs);
                    var sw = Stopwatch.StartNew();
                    var exitCode = app.Run(filteredArgs);
                    sw.Stop();
                    CliInvocationLog.Append(cliLog, commandLine, exitCode, sw.Elapsed.TotalMilliseconds);
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

        CrashLog.Write($"[{DateTime.UtcNow:O}] Tendril starting (PID {Environment.ProcessId}) | {GetMemoryStats()}");

        // Install native console control handler FIRST — this catches CTRL_CLOSE_EVENT
        // (console window closed), CTRL_C_EVENT, CTRL_BREAK_EVENT, CTRL_LOGOFF_EVENT,
        // and CTRL_SHUTDOWN_EVENT. Logging here tells us exactly WHY the process is dying.
        try
        {
            Console.CancelKeyPress += (_, _) =>
            {
                Environment.Exit(0);
            };
        }
        catch { }

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

                if (ctrlType is 0 or 1 or 2)
                {
                    Environment.Exit(0);
                    return true;
                }

                return false;
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

            var version = typeof(Program).Assembly.GetName().Version;
            var versionString = version?.ToString(3) ?? "1.1.12";

            var window = new DesktopWindow(server)
                .Title("Ivy Tendril")
                .AppId("Ivy Tendril")
                .Size(1800, 1200)
                .UseDpiScaling(false)
                .Icon(typeof(Program), iconResource)
                .AboutName("Ivy Tendril")
                .AboutVersion(versionString)
                .AboutCopyright("© 2026 Ivy Interactive")
                .AboutWebsite("https://ivy.app")
                .AboutLicense("FSL")
                .AboutAuthor("Ivy Interactive")
                .AboutComments("Tendril is an end-to-end AI coding agent orchestrator built on the Ivy Framework that manages AI coding plans, tracks costs, and automates pull request generation.")
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
        bool notMaster = args.Contains("--not-master") || args.Contains("--slave");

        if (verbose)
            Environment.SetEnvironmentVariable("TENDRIL_VERBOSE", "1");
        if (quiet)
            Environment.SetEnvironmentVariable("TENDRIL_QUIET", "1");
        if (beta)
            Environment.SetEnvironmentVariable("TENDRIL_BETA", "1");
        if (notMaster)
            Environment.SetEnvironmentVariable("TENDRIL_NOT_MASTER", "1");

        var filtered = args.Where(a =>
            a != "--desktop" && a != "--web" &&
            a != "--verbose" && a != "-v" &&
            a != "--quiet" && a != "-q" &&
            a != "--beta" &&
            a != "--not-master" && a != "--slave" &&
            a != DetachedLaunchMarker).ToArray();

        return (verbose, quiet, forceDesktop, forceWeb, beta, filtered);
    }

    private static bool IsPackagedApp()
    {
        try
        {
            return Velopack.Locators.VelopackLocator.Current?.CurrentlyInstalledVersion != null;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
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
        // If the executing assembly is in the .store or .dotnet/tools folder, it's a global tool invocation
        var path = System.AppContext.BaseDirectory;
        var toolsFolder = Path.Combine(".dotnet", "tools");
        if (path.Contains(".store", StringComparison.OrdinalIgnoreCase) ||
            path.Contains(toolsFolder, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var processPathName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
        if (processPathName.Equals("tendril", StringComparison.OrdinalIgnoreCase))
            return true;

        var argv0 = Environment.GetCommandLineArgs().FirstOrDefault() ?? string.Empty;
        var argv0Name = Path.GetFileNameWithoutExtension(argv0);
        return argv0Name.Equals("tendril", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// When this process is the stale `dotnet tool install` copy of tendril and a newer
    /// installer-managed CLI is present, forwards the invocation to that CLI and returns its
    /// exit code. Returns null when the run should proceed normally in this process.
    /// </summary>
    private static int? TryRedirectLegacyToolInvocation(string[] args)
    {
        if (Environment.GetEnvironmentVariable("TENDRIL_NO_LEGACY_REDIRECT") == "1")
            return null;
        if (args.Contains(DetachedLaunchMarker))
            return null;

        try
        {
            if (!TendrilInstallHelper.IsLegacyDotnetToolProcess())
                return null;

            var installedCli = TendrilInstallHelper.FindInstalledCli();
            if (installedCli == null)
                return null;

            if (Environment.GetEnvironmentVariable("TENDRIL_QUIET") != "1")
            {
                var version = TendrilInstallHelper.GetLegacyToolVersion();
                Console.Error.WriteLine(
                    $"Warning: you are running the outdated Ivy.Tendril .NET tool (v{version ?? "unknown"}). " +
                    $"Forwarding to the installed version at {installedCli}. " +
                    "Remove the stale tool with: dotnet tool uninstall --global Ivy.Tendril");
            }

            var psi = new ProcessStartInfo
            {
                FileName = installedCli,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);
            psi.Environment["TENDRIL_NO_LEGACY_REDIRECT"] = "1";

            using var process = Process.Start(psi);
            if (process == null)
                return null;

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            CrashLog.Write($"[Program] TryRedirectLegacyToolInvocation failed: {ex}");
            return null;
        }
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

    internal static CommandApp ConfigureCliCommands(ServiceCollection cliServices, IAnsiConsole? console = null)
    {
        var registrar = new TypeRegistrar(cliServices);
        var app = new CommandApp(registrar);
        app.Configure(config =>
        {
            config.PropagateExceptions();
            if (console != null)
                config.Settings.Console = console;

            // Doctor command
            config.AddCommand<DoctorCliCommand>("doctor")
                .WithDescription("System health check");

            // Project analyzer command
            config.AddCommand<ProjectAnalyzerCommand>("project-analyzer")
                .WithDescription("Analyze a folder and print a YAML stack report");

            // Generate certificates command (hidden, for build time)
            config.AddCommand<GenerateCertsCommand>("generate-certs")
                .WithDescription("Generate self-signed localhost certificate for desktop HTTPS")
                .IsHidden();

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
                pw.AddCommand<PromptwareListMemoryCommand>("list-memory")
                    .WithDescription("List a promptware's memory files");
                pw.AddCommand<PromptwareWriteMemoryCommand>("write-memory")
                    .WithDescription("Write a promptware memory file from STDIN");
                pw.AddCommand<PromptwareDeleteMemoryCommand>("delete-memory")
                    .WithDescription("Delete a promptware memory file that is no longer true");
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
                job.AddCommand<JobListCommand>("list")
                    .WithDescription("List jobs with optional filters");
                job.AddCommand<JobStatusCommand>("status")
                    .WithDescription("Report job status (message, planId, planTitle)");
                job.AddCommand<JobFailCommand>("fail")
                    .WithDescription("Report a job failure with a descriptive message");
                job.AddCommand<JobCancelCommand>("cancel")
                    .WithDescription("Cancel a running or queued job, terminate its process, and revert plan state");
                job.AddCommand<JobStartCommand>("start")
                    .WithDescription("Start a job via the running Tendril server");
                job.AddCommand<JobAddLogCommand>("add-log")
                    .WithDescription("Append a log entry to the job's log");
            });

            // Plan management commands
            config.AddBranch("plan", plan =>
            {
                plan.AddCommand<PlanListCommand>("list")
                    .WithDescription("List plans with optional filters");
                plan.AddCommand<PlanCreateCommand>("create")
                    .WithDescription("Create a new plan");
                plan.AddCommand<PlanUpdateCommand>("update")
                    .WithDescription("Update plan from a file or STDIN");
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
                plan.AddCommand<PlanWriteRevisionCommand>("write-revision")
                    .WithDescription("Write a revision file from a file or STDIN");
                plan.AddCommand<PlanGetRevisionCommand>("get-revision")
                    .WithDescription("Print revision content");
                plan.AddCommand<PlanValidateCommand>("validate")
                    .WithDescription("Validate plan health");
                plan.AddCommand<PlanCleanupCommand>("cleanup")
                    .WithDescription("Remove worktrees from a plan");
                plan.AddCommand<PlanAddWorktreeCommand>("add-worktree")
                    .WithDescription("Create a git worktree for a plan");
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
                project.AddCommand<ProjectListMcpCommand>("list-mcp")
                    .WithDescription("List MCP servers in a project");
                project.AddCommand<ProjectAddMcpCommand>("add-mcp")
                    .WithDescription("Add an MCP server to a project");
                project.AddCommand<ProjectRemoveMcpCommand>("remove-mcp")
                    .WithDescription("Remove an MCP server from a project");
                project.AddCommand<ProjectListSkillsCommand>("list-skills")
                    .WithDescription("List custom skills in a project");
                project.AddCommand<ProjectAddSkillCommand>("add-skill")
                    .WithDescription("Add a custom skill to a project");
                project.AddCommand<ProjectRemoveSkillCommand>("remove-skill")
                    .WithDescription("Remove a custom skill from a project");
                project.AddCommand<ProjectImportCommand>("import")
                    .WithDescription("Import MCP servers and custom skills from a repository into a project");
                project.AddCommand<ProjectImportMcpCommand>("import-mcp")
                    .WithDescription("Import MCP servers from a repository into a project");
                project.AddCommand<ProjectImportSkillsCommand>("import-skills")
                    .WithDescription("Import custom skills from a repository into a project");
            });

            config.AddBranch("config", cfg =>
            {
                cfg.AddCommand<ConfigGetCommand>("get")
                    .WithDescription("Get a top-level config value");
                cfg.AddCommand<ConfigSetCommand>("set")
                    .WithDescription("Set a top-level config value");
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

    internal static bool IsPortInUse(int port)
    {
        // Kestrel can bind the IPv6 loopback only, leaving the IPv4 probe below to report
        // "free" even though the port is taken — so a bind failure on either family counts.
        return IsPortBoundOn(System.Net.IPAddress.Loopback, port)
            || IsPortBoundOn(System.Net.IPAddress.IPv6Loopback, port);
    }

    private static bool IsPortBoundOn(System.Net.IPAddress address, int port)
    {
        try
        {
            var listener = new System.Net.Sockets.TcpListener(address, port);
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
