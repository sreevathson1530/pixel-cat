using System.Windows;
using Pixel.App.Cat;
using Pixel.Core.Art;

namespace Pixel.App;

public partial class App : Application
{
    private CatController? _cat;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        _cat = new CatController(scale: 3, Palette.Black, hwnd => hwnd == _cat?.Handle);
        _cat.Start();
    }
}
