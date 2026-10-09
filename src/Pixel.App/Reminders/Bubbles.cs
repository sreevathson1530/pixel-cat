using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Pixel.App.Native;
using Pixel.App.Shell;

namespace Pixel.App.Reminders;

/// <summary>Shared look for the little cards that float above the cat (speech bubble, Pomodoro badge).</summary>
internal abstract class FloatingCard : Window
{
    protected static readonly Brush Ink = Hex("#15131c"), Text = Hex("#ebe8f4"), Accent = Hex("#c6e33c");
    protected readonly TextBlock Label = new() { Foreground = Text, TextWrapping = TextWrapping.Wrap, FontSize = 13 };

    protected FloatingCard(double maxWidth)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Label.MaxWidth = maxWidth;
        Content = new Border
        {
            Background = Ink,
            BorderBrush = Accent,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(8),
            Child = Label,
            Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.45 },
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        OwnWindows.Add(hwnd);
        Win32.AddExStyle(hwnd, Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE);
        if (Environment.GetEnvironmentVariable("PIXEL_DEBUG_CAPTURE") != "1")
            Win32.SetWindowDisplayAffinity(hwnd, Win32.WDA_EXCLUDEFROMCAPTURE);
    }

    /// <summary>Centres the card above <paramref name="cat"/> (DIPs), kept inside the work area.</summary>
    public void PlaceAbove(Rect cat, double lift)
    {
        UpdateLayout();
        var area = SystemParameters.WorkArea;
        Left = Math.Clamp(cat.Left + cat.Width / 2 - ActualWidth / 2, area.Left, area.Right - ActualWidth);
        Top = Math.Clamp(cat.Top + cat.Height * 0.3 - ActualHeight - lift, area.Top, area.Bottom - ActualHeight);
    }

    private static SolidColorBrush Hex(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}

/// <summary>What the cat "says" when a reminder fires. Click to dismiss; fades after a few seconds.</summary>
internal sealed class SpeechBubble : FloatingCard
{
    private readonly DispatcherTimer _hide = new();

    public SpeechBubble() : base(240)
    {
        _hide.Tick += (_, _) =>
        {
            _hide.Stop();
            Hide();
        };
        MouseLeftButtonUp += (_, _) => Hide();
        Cursor = System.Windows.Input.Cursors.Hand;
    }

    public void Say(string text, Rect cat, TimeSpan duration)
    {
        Label.Text = text;
        if (!IsVisible) Show();
        PlaceAbove(cat, lift: 0);
        _hide.Stop();
        _hide.Interval = duration;
        _hide.Start();
    }
}

/// <summary>Small Pomodoro countdown that rides above the cat while a session runs.</summary>
internal sealed class TimerBadge : FloatingCard
{
    public TimerBadge() : base(200)
    {
        Label.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        Label.FontSize = 12;
        IsHitTestVisible = false;
    }

    public void Show(string text, Rect cat, bool bubbleVisible)
    {
        Label.Text = text;
        if (!IsVisible) Show();
        PlaceAbove(cat, lift: bubbleVisible ? 64 : 0);
    }
}
