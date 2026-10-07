using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Pixel.App.Native;
using Pixel.App.Shell;
using Pixel.Core.Brain;
using Pixel.Core.Storage;

namespace Pixel.App.History;

/// <summary>Browse, search and delete past conversations (stored only on this PC).</summary>
internal sealed class HistoryWindow : Window
{
    private static readonly Brush Bg = Hex("#15131c"), Surface = Hex("#1d1b27"), Border2 = Hex("#322e44"),
        Text = Hex("#ebe8f4"), Muted = Hex("#9d98b3"), UserBubble = Hex("#2d2944");

    private readonly HistoryStore _store;
    private readonly ListBox _list = new();
    private readonly StackPanel _messages = new() { Margin = new Thickness(16) };
    private readonly TextBox _search = new();
    private long? _selected;

    public HistoryWindow(HistoryStore store)
    {
        _store = store;
        Title = "Pixel — History";
        Width = 860;
        Height = 560;
        MinWidth = 600;
        MinHeight = 400;
        Background = Bg;
        Foreground = Text;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13.5;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _search.Background = Surface;
        _search.Foreground = Text;
        _search.CaretBrush = Text;
        _search.BorderBrush = Border2;
        _search.Padding = new Thickness(8, 6, 8, 6);
        _search.ToolTip = "Search your chats";
        _search.TextChanged += (_, _) => Reload();

        _list.Background = Bg;
        _list.Foreground = Text;
        _list.BorderThickness = new Thickness(0);
        _list.SelectionChanged += (_, _) => ShowSelected();
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);

        var left = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(_search, Dock.Top);
        left.Children.Add(_search);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(MakeButton("Delete chat", (_, _) => DeleteSelected()));
        buttons.Children.Add(MakeButton("Delete all", (_, _) => DeleteAll()));
        DockPanel.SetDock(buttons, Dock.Bottom);
        left.Children.Add(buttons);
        _list.Margin = new Thickness(0, 8, 0, 0);
        left.Children.Add(_list);

        var right = new ScrollViewer { Content = _messages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Surface };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        Content = grid;

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
        var items = string.IsNullOrWhiteSpace(_search.Text) ? _store.List() : _store.Search(_search.Text.Trim());
        _list.Items.Clear();
        foreach (var c in items)
        {
            var panel = new StackPanel { Margin = new Thickness(4, 6, 4, 6) };
            panel.Children.Add(new TextBlock { Text = c.Title, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Text });
            panel.Children.Add(new TextBlock
            {
                Text = $"{c.StartedAt.LocalDateTime:ddd d MMM, HH:mm} · {c.MessageCount} messages",
                Foreground = Muted,
                FontSize = 11.5,
            });
            _list.Items.Add(new ListBoxItem { Content = panel, Tag = c.Id });
        }
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        else _messages.Children.Clear();
    }

    private void ShowSelected()
    {
        _messages.Children.Clear();
        if (_list.SelectedItem is not ListBoxItem { Tag: long id }) return;
        _selected = id;
        foreach (var m in _store.Messages(id))
        {
            var user = m.Role == ChatRole.User;
            _messages.Children.Add(new Border
            {
                Background = user ? UserBubble : Brushes.Transparent,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(user ? 12 : 0, 8, user ? 12 : 0, 8),
                Margin = new Thickness(user ? 80 : 0, 0, 0, 10),
                HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
                Child = new TextBox
                {
                    Text = m.Content,
                    TextWrapping = TextWrapping.Wrap,
                    IsReadOnly = true,
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    Foreground = Text,
                    FontFamily = m.Content.Contains("```") ? new FontFamily("Cascadia Mono, Consolas") : FontFamily,
                },
            });
        }
    }

    private void DeleteSelected()
    {
        if (_selected is not { } id) return;
        _store.Delete(id);
        _selected = null;
        Reload();
    }

    private void DeleteAll()
    {
        var answer = MessageBox.Show(this, "Delete all saved chats? This cannot be undone.", "Pixel",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        _store.DeleteAll();
        Reload();
    }

    private static Button MakeButton(string text, RoutedEventHandler click)
    {
        var b = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(12, 5, 12, 5),
            Background = Surface,
            Foreground = Text,
            BorderBrush = Border2,
        };
        b.Click += click;
        return b;
    }

    private static SolidColorBrush Hex(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
