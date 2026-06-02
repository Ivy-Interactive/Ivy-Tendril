using Ivy.Desktop;

namespace Ivy.Tendril.Updater;

internal static class Program
{
    [STAThread]
    public static int Main()
    {
        var server = new Server(new ServerArgs()
        {
            Port = 5010,
            Silent = true,
            DefaultAppId = "loading"
        });

        server.AddAppsFromAssembly();
        server.UseHotReload();

        var iconResource = OperatingSystem.IsWindows() ? "Ivy.Tendril.Updater.icon.ico"
            : OperatingSystem.IsMacOS() ? "Ivy.Tendril.Updater.icon.icns"
            : "Ivy.Tendril.Updater.icon.png";

        var window = new DesktopWindow(server)
            .Title("Ivy Tendril Updater")
            .Size(800, 600)
            .Resizable(false)
            .TopMost(true)
            .UseDpiScaling(true)
            .UseDevTools(false)
            .Icon(typeof(Program), iconResource);

        return window.Run();
    }
}
