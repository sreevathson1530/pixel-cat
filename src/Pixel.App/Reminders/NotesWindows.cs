using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Pixel.App.Native;
using Pixel.App.Shell;
using Pixel.Core.Reminders;

namespace Pixel.App.Reminders;

/// <summary>A pinned note on the desktop. Drag to move; × unpins it.</summary>
internal sealed class StickyNoteWindow : Window
{
    public StickyNoteWindow(Note note, Action<Note> unpin)
    {
        Note = note;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = 220;
        SizeToContent = SizeToContent.Height;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var close = new Button
        {
            Content = "×",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Hex("#6b5a1e"),
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Right,
            ToolTip = "Unpin",
            Cursor = Cursors.Hand,
        };
        close.Click += (_, _) => unpin(Note);

        var body = new TextBlock
        {
            Text = note.Text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Hex("#3b3110"),
            FontSize = 13.5,
            Margin = new Thickness(12, 0, 12, 12),
        };
        var stack = new StackPanel();
        stack.Children.Add(close);
        stack.Children.Add(body);
        Content = new Border
        {
            Background = Hex("#fff1a8"),
            BorderBrush = Hex("#e8cf5c"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = stack,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.35 },
            Margin = new Thickness(6),
        };
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
    }

    public Note Note { get; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        OwnWindows.Add(hwnd);
        Win32.AddExStyle(hwnd, Win32.WS_EX_TOOLWINDOW);
    }

    private static SolidColorBrush Hex(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}

/// <summary>Lists notes: add, pin/unpin, delete.</summary>
internal sealed class NotesWindow : Window
{
    private static readonly Brush Bg = Hex("#15131c"), Surface = Hex("#1d1b27"), Border2 = Hex("#322e44"),
        Text = Hex("#ebe8f4"), Muted = Hex("#9d98b3");

    private readonly ReminderService _service;
    private readonly StackPanel _list = new() { Margin = new Thickness(12) };
    private readonly TextBox _input = new();

    public NotesWindow(ReminderService service)
    {
        _service = service;
        Title = "Pixel — Notes";
        Width = 420;
        Height = 520;
        Background = Bg;
        Foreground = Text;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13.5;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _input.Background = Surface;
        _input.Foreground = Text;
        _input.CaretBrush = Text;
        _input.BorderBrush = Border2;
        _input.Padding = new Thickness(8, 6, 8, 6);
        _input.ToolTip = "Type a note and press Enter";
        _input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(_input.Text)) return;
            _service.AddNote(_input.Text.Trim(), pinned: false);
            _input.Clear();
        };

        var dock = new DockPanel();
        DockPanel.SetDock(_input, Dock.Top);
        _input.Margin = new Thickness(12, 12, 12, 0);
        dock.Children.Add(_input);
        dock.Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = dock;

        _service.NotesChanged += Reload;
        Closed += (_, _) => _service.NotesChanged -= Reload;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
        Reload();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        OwnWindows.Add(hwnd);
        var dark = 1;
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    private void Reload()
    {
        _list.Children.Clear();
        if (_service.Notes.Count == 0)
        {
            _list.Children.Add(new TextBlock { Text = "No notes yet. Ask Pixel to \"note that…\" or type one above.", Foreground = Muted, TextWrapping = TextWrapping.Wrap });
            return;
        }
        foreach (var note in _service.Notes.OrderByDescending(n => n.Created))
        {
            var pin = new CheckBox { Content = "Pinned", IsChecked = note.Pinned, Foreground = Muted, Margin = new Thickness(0, 6, 12, 0) };
            pin.Click += (_, _) => _service.SetPinned(note.Id, pin.IsChecked == true);
            var delete = new Button { Content = "Delete", Padding = new Thickness(10, 2, 10, 2), Background = Surface, Foreground = Text, BorderBrush = Border2, Margin = new Thickness(0, 4, 0, 0) };
            delete.Click += (_, _) => _service.DeleteNote(note.Id);
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(pin);
            actions.Children.Add(delete);
            var card = new StackPanel();
            card.Children.Add(new TextBlock { Text = note.Text, TextWrapping = TextWrapping.Wrap, Foreground = Text });
            card.Children.Add(new TextBlock { Text = note.Created.LocalDateTime.ToString("ddd d MMM, HH:mm"), Foreground = Muted, FontSize = 11.5 });
            card.Children.Add(actions);
            _list.Children.Add(new Border
            {
                Background = Surface,
                BorderBrush = Border2,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8),
                Child = card,
            });
        }
    }

    private static SolidColorBrush Hex(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
