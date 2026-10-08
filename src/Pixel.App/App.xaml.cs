using System.Windows;
using Pixel.App.Shell;

namespace Pixel.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private AppHost? _host;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        // Diagnostic: "Pixel.exe --resolve-app chrome" logs what an open_app request would launch, then exits.
        if (e.Args is ["--resolve-app", .. var names])
        {
            foreach (var name in names)
                Log.Info($"resolve-app '{name}' → {Tools.AppResolver.Resolve(name)?.Describe() ?? "(refused / not found)"}");
            Shutdown();
            return;
        }

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
