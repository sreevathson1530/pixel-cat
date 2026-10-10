using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Pixel.App.Native;
using Pixel.App.Shell;
using Pixel.Core.Art;
using Pixel.Core.Storage;

namespace Pixel.App.Preferences;

/// <summary>Pixel's settings: look, sounds, brain, helpers and AI-tool reactions.</summary>
internal sealed class SettingsWindow : Window
{
    private static readonly Brush Bg = Hex("#15131c"), Surface = Hex("#1d1b27"), Border2 = Hex("#322e44"),
        Text = Hex("#ebe8f4"), Muted = Hex("#9d98b3"), Accent = Hex("#c6e33c");

    private readonly AppHost _host;
    private readonly ComboBox _palette = new(), _model = new(), _backend = new();
    private readonly Slider _scale = new() { Minimum = 2, Maximum = 5, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly Slider _volume = new() { Minimum = 0, Maximum = 1 };
    private readonly CheckBox _muted = new(), _errors = new(), _autostart = new(), _freeRoam = new();
    private readonly TextBox _water = new(), _stretch = new(), _threads = new(), _agents = new();

    public SettingsWindow(AppHost host)
    {
        _host = host;
        var s = host.Settings;
        Title = "Pixel — Settings";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Background = Bg;
        Foreground = Text;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13.5;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        foreach (var name in Palette.Names) _palette.Items.Add(name);
        _palette.SelectedItem = s.Palette;
        _scale.Value = s.Scale;
        _freeRoam.IsChecked = s.FreeRoam;
        _muted.IsChecked = s.Muted;
        _volume.Value = s.Volume;
        foreach (var m in ModelChoices(s)) _model.Items.Add(m);
        _model.SelectedItem = _model.Items.Cast<ModelChoice>().FirstOrDefault(m => m.Path.Equals(CurrentModel(s), StringComparison.OrdinalIgnoreCase));
        foreach (var b in new[] { "cpu", "vulkan", "sycl", "openvino" }.Where(b => Brain.LlamaPaths.FindServerExe(b) is not null)) _backend.Items.Add(b);
        _backend.SelectedItem = s.LlamaBackend;
        _threads.Text = s.LlamaThreads.ToString();
        _water.Text = s.WaterReminderMinutes.ToString();
        _stretch.Text = s.StretchReminderMinutes.ToString();
        _errors.IsChecked = s.ProactiveErrors;
        _agents.Text = s.AgentProcesses;
        _autostart.IsChecked = AutoStart.IsEnabled();

        var stack = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
        stack.Children.Add(Section("Look"));
        stack.Children.Add(Row("Fur colour", Themed(_palette)));
        stack.Children.Add(Row("Size (restart to apply)", _scale));
        stack.Children.Add(Row("Walk anywhere on screen", Themed(_freeRoam)));
        stack.Children.Add(Hint("Off: Pixel stays on the floor and hops onto window tops instead."));
        stack.Children.Add(Section("Sounds"));
        stack.Children.Add(Row("Mute", Themed(_muted)));
        stack.Children.Add(Row("Volume", _volume));
        stack.Children.Add(Section("Brain"));
        stack.Children.Add(Row("Model", Themed(_model)));
        stack.Children.Add(Hint("The 7B answers ~3× faster; the 14B is smarter but needs ~9 GB of free memory."));
        stack.Children.Add(Row("Engine", Themed(_backend)));
        stack.Children.Add(Row("CPU threads (0 = auto)", Themed(_threads)));
        stack.Children.Add(Section("Helpers"));
        stack.Children.Add(Row("Water reminder (min, 0 = off)", Themed(_water)));
        stack.Children.Add(Row("Stretch reminder (min, 0 = off)", Themed(_stretch)));
        stack.Children.Add(Row("Spot errors on screen", Themed(_errors)));
        stack.Children.Add(Section("AI coding tools"));
        stack.Children.Add(Row("Watch these apps", Themed(_agents)));
        var connect = MakeButton("Connect Claude Code…", false);
        connect.Click += (_, _) => host.Agents?.ConnectClaudeCode();
        stack.Children.Add(Row("Claude Code", connect));
        stack.Children.Add(Section("System"));
        stack.Children.Add(Row("Start with Windows", Themed(_autostart)));

        var save = MakeButton("Save", true);
        save.Click += (_, _) => Save();
        var cancel = MakeButton("Cancel", false);
        cancel.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        stack.Children.Add(buttons);
        Content = stack;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    private sealed record ModelChoice(string Path, string Label)
    {
        public override string ToString() => Label;
    }

    private static string CurrentModel(Pixel.Core.Storage.Settings s) =>
        Pixel.Core.Brain.ModelLocator.Find(s.ModelDirectory, s.ModelPath) is Pixel.Core.Brain.ModelStatus.Found f ? f.Path : s.ModelPath ?? "";

    /// <summary>Every model in the usual folders, labelled with its size and whether it can see pictures.</summary>
    private static IEnumerable<ModelChoice> ModelChoices(Pixel.Core.Storage.Settings s)
    {
        var dirs = new[]
        {
            s.ModelDirectory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "pixel-models"),
            Path.GetDirectoryName(s.ModelPath ?? "") ?? "",
        };
        return dirs.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.gguf"))
            .Where(p => !Pixel.Core.Brain.ModelLocator.IsProjector(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => new ModelChoice(p, $"{Path.GetFileNameWithoutExtension(p)}  ({new FileInfo(p).Length / (double)(1L << 30):0.0} GB"
                                            + (Pixel.Core.Brain.ModelLocator.ProjectorFor(p) is null ? ", reads text)" : ", sees pictures)")));
    }

    private void Save()
    {
        static int Int(TextBox t, int fallback, int min, int max) =>
            int.TryParse(t.Text.Trim(), out var v) ? Math.Clamp(v, min, max) : fallback;

        var old = _host.Settings;
        var model = (_model.SelectedItem as ModelChoice)?.Path ?? old.ModelPath;
        var next = old with
        {
            Palette = _palette.SelectedItem as string ?? old.Palette,
            Scale = (int)_scale.Value,
            FreeRoam = _freeRoam.IsChecked == true,
            Muted = _muted.IsChecked == true,
            Volume = Math.Round(_volume.Value, 2),
            ModelPath = model,
            LlamaBackend = _backend.SelectedItem as string ?? old.LlamaBackend,
            LlamaThreads = Int(_threads, old.LlamaThreads, 0, 64),
            WaterReminderMinutes = Int(_water, old.WaterReminderMinutes, 0, 600),
            StretchReminderMinutes = Int(_stretch, old.StretchReminderMinutes, 0, 600),
            ProactiveErrors = _errors.IsChecked == true,
            AgentProcesses = _agents.Text.Trim(),
            StartWithWindows = _autostart.IsChecked == true,
        };
        if (next.StartWithWindows != AutoStart.IsEnabled()) AutoStart.Set(next.StartWithWindows);
        var brainChanged = next.ModelPath != old.ModelPath || next.LlamaBackend != old.LlamaBackend || next.LlamaThreads != old.LlamaThreads;
        _host.UpdateSettings(next);
        if (brainChanged) _host.Brain.Unload(); // next question loads the new choice
        Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        OwnWindows.Add(hwnd);
        var dark = 1;
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    private static TextBlock Section(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        Foreground = Accent,
        FontSize = 11.5,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 14, 0, 6),
    };

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        Foreground = Muted,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 6),
    };

    private static Grid Row(string label, FrameworkElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var l = new TextBlock { Text = label, Foreground = Text, VerticalAlignment = VerticalAlignment.Center };
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        g.Children.Add(l);
        g.Children.Add(control);
        return g;
    }

    private static T Themed<T>(T c) where T : Control
    {
        c.Foreground = Text;
        if (c is TextBox t)
        {
            t.Background = Surface;
            t.BorderBrush = Border2;
            t.CaretBrush = Text;
            t.Padding = new Thickness(6, 4, 6, 4);
        }
        return c;
    }

    private static Button MakeButton(string text, bool primary) => new()
    {
        Content = text,
        Padding = new Thickness(16, 6, 16, 6),
        Margin = new Thickness(8, 0, 0, 0),
        Background = primary ? Accent : Surface,
        Foreground = primary ? Hex("#1a1d08") : Text,
        BorderBrush = primary ? Accent : Border2,
        FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
    };

    private static SolidColorBrush Hex(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
