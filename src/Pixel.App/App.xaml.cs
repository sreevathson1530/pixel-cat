using System.Windows;
using Pixel.App.Shell;

namespace Pixel.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private AppHost? _host;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\Pixel.DesktopCat", out var first);
        if (!first)
        {
            Shutdown();
            return;
        }

        _host = new AppHost();
        _host.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
