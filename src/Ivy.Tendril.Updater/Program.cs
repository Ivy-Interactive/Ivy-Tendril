using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Photino.NET;

namespace Ivy.Tendril.Updater;

internal static class Program
{
    [STAThread]
    public static int Main()
    {
        return Updater().GetAwaiter().GetResult();
    }

    private static async Task<int> Updater()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-US");

        var port = 5010;
        var url = $"https://localhost:{port}";
        var server = new Server(new ServerArgs()
        {
            Port = port,
            Silent = true,
            DefaultAppId = "loading"
        });

        server.AddAppsFromAssembly();
        server.UseHotReload();

        CancellationTokenSource cts = new CancellationTokenSource();

        var serverTask = server.RunAsync(cts);

        if (!await CheckIfPortIsListening(port))
        {
            Console.WriteLine($@"Error: Unable to connect to {url}. Something went wrong.");
            return 1;
        }

        var window = new PhotinoWindow() { LogVerbosity = 0 };

        var scalingFactor = DpiDetector.GetSystemScalingFactor();
        var baseWidth = 800;
        var baseHeight = 600;
        var windowWidth = (int)(baseWidth * scalingFactor);
        var windowHeight = (int)(baseHeight * scalingFactor);

        var iconPath = GetIconPath();

        window
            .SetUseOsDefaultSize(false)
            .SetSize(windowWidth, windowHeight)
            .SetTitle("Ivy Tendril Updater")
            .SetIconFile(iconPath)
            .SetResizable(false)
            .SetTopMost(true)
            .SetJavascriptClipboardAccessEnabled(false)
            .SetDevToolsEnabled(false)
            .SetIgnoreCertificateErrorsEnabled(true)
            .SetWebSecurityEnabled(false)
            .Center()
            .Load(new Uri(url));

        window.WaitForClose();

        await cts.CancelAsync();
        await serverTask;

        return 0;
    }

    private static string GetIconPath()
    {
        var baseDir = System.AppContext.BaseDirectory;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(baseDir, "icon.ico");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return Path.Combine(baseDir, "icon.icns");
        return Path.Combine(baseDir, "icon.png");
    }

    private static async Task<bool> CheckIfPortIsListening(int port, int maxAttempts = 10)
    {
        var delayMs = 1000;

        for (var i = 0; i < maxAttempts; i++)
        {
            try
            {
                bool isListening = IPGlobalProperties
                    .GetIPGlobalProperties()
                    .GetActiveTcpListeners()
                    .Any(endpoint => endpoint.Port == port);
                if (isListening)
                {
                    return true;
                }
            }
            catch
            {
                //ignore
            }
            if (i == maxAttempts - 1) return false;
            await Task.Delay(delayMs);
        }

        return false;
    }
}
