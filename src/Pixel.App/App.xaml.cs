using System.Windows;

namespace Pixel.App;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        Shutdown();
    }
}
