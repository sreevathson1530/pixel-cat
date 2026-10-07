using System.Drawing;
using System.Windows.Forms;
using Pixel.Core.Art;

namespace Pixel.App.Shell;

/// <summary>System-tray icon (the cat's face) with Pixel's menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _showHide, _stay, _autoStart;

    public TrayIcon(Palette palette)
    {
        _showHide = new ToolStripMenuItem("Hide Pixel", null, (_, _) => ToggleVanish?.Invoke()) { ShortcutKeyDisplayString = "Ctrl+RShift+Del" };
        _stay = new ToolStripMenuItem("Stay put", null, (_, _) => ToggleStay?.Invoke());
        _autoStart = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleAutoStart?.Invoke());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_showHide);
        menu.Items.Add(new ToolStripMenuItem("Chat", null, (_, _) => ToggleChat?.Invoke()) { ShortcutKeyDisplayString = "Ctrl+Win+Space" });
        menu.Items.Add(_stay);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("History…", null, (_, _) => OpenHistory?.Invoke()));
        menu.Items.Add(new ToolStripMenuItem("Notes…", null, (_, _) => OpenNotes?.Invoke()));
        menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => OpenSettings?.Invoke()));
        menu.Items.Add(new ToolStripMenuItem("Connect Claude Code…", null, (_, _) => ConnectClaude?.Invoke()));
        menu.Items.Add(_autoStart);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quit Pixel", null, (_, _) => Quit?.Invoke()));
        menu.Opening += (_, _) => Opening?.Invoke();

        _icon = new NotifyIcon { Icon = MakeIcon(palette), Text = "Pixel", ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => ToggleChat?.Invoke();
    }

    public event Action? ToggleVanish, ToggleChat, ToggleStay, ToggleAutoStart, OpenHistory, OpenNotes, OpenSettings,
        ConnectClaude, Quit, Opening;

    public void SetState(bool visible, bool staying, bool autoStart)
    {
        _showHide.Text = visible ? "Hide Pixel" : "Show Pixel";
        _stay.Checked = staying;
        _stay.Enabled = visible;
        _autoStart.Checked = autoStart;
    }

    /// <summary>Windows notification (used for reminders while Pixel is hidden).</summary>
    public void Notify(string title, string text) => _icon.ShowBalloonTip(8000, title, text, ToolTipIcon.None);

    /// <summary>The cat's head cropped from the sitting sprite, scaled up with crisp pixels.</summary>
    private static Icon MakeIcon(Palette palette)
    {
        var px = CatRenderer.Render(new CatPose(BodyShape.Sit), palette);
        using var bmp = new Bitmap(32, 32);
        for (var y = 0; y < 16; y++)
        for (var x = 0; x < 16; x++)
        {
            var p = px[(9 + y) * CatRenderer.Width + 18 + x];
            var c = Color.FromArgb((int)(p >> 24), (int)((p >> 16) & 0xFF), (int)((p >> 8) & 0xFF), (int)(p & 0xFF));
            for (var dy = 0; dy < 2; dy++)
            for (var dx = 0; dx < 2; dx++)
                bmp.SetPixel(x * 2 + dx, y * 2 + dy, c);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
