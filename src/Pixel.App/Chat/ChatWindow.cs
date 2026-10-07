using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Pixel.App.Native;
using Pixel.App.Shell;

namespace Pixel.App.Chat;

/// <summary>Borderless dark window hosting the WebView2 chat UI from the local web\ folder.</summary>
internal sealed class ChatWindow : Window
{
    private const string Host = "pixel.local";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly WebView2 _web = new() { DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x15, 0x13, 0x1C) };
    private readonly TaskCompletionSource _ready = new();

    public ChatWindow()
    {
        Title = "Pixel chat";
        Width = 380;
        Height = 540;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0x15, 0x13, 0x1C));
        Content = _web;
    }

    public IntPtr Handle { get; private set; }

    /// <summary>Raised for each JSON message from the page (on the UI thread).</summary>
    public event Action<JsonElement>? Message;

    /// <summary>Completes when the page has loaded and said "ready".</summary>
    public Task Ready => _ready.Task;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Handle = new WindowInteropHelper(this).Handle;
        OwnWindows.Add(Handle);
        Win32.AddExStyle(Handle, Win32.WS_EX_TOOLWINDOW);
        var round = 2; // DWMWCP_ROUND
        Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        var dark = 1;
        Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        if (Environment.GetEnvironmentVariable("PIXEL_DEBUG_CAPTURE") != "1")
            Win32.SetWindowDisplayAffinity(Handle, Win32.WDA_EXCLUDEFROMCAPTURE);
    }

    public async Task InitializeAsync()
    {
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pixel", "WebView2");
        var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
        await _web.EnsureCoreWebView2Async(env);
        var core = _web.CoreWebView2;
        var debug = Environment.GetEnvironmentVariable("PIXEL_DEBUG_CAPTURE") == "1";
        core.Settings.AreDevToolsEnabled = debug;
        core.Settings.AreDefaultContextMenusEnabled = debug;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;

        var webDir = Path.Combine(AppContext.BaseDirectory, "web");
        core.SetVirtualHostNameToFolderMapping(Host, webDir, CoreWebView2HostResourceAccessKind.Deny);
        core.NavigationStarting += (_, a) =>
        {
            if (!a.Uri.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase)) a.Cancel = true;
        };
        core.NewWindowRequested += (_, a) => a.Handled = true;
        core.WebMessageReceived += (_, a) =>
        {
            using var doc = JsonDocument.Parse(a.WebMessageAsJson);
            var msg = doc.RootElement.Clone();
            if (msg.TryGetProperty("type", out var t) && t.GetString() == "ready") _ready.TrySetResult();
            Message?.Invoke(msg);
        };
        core.Navigate($"https://{Host}/index.html");
    }

    public void Post(object message)
    {
        if (_web.CoreWebView2 is { } core) core.PostWebMessageAsJson(JsonSerializer.Serialize(message, Json));
    }

    /// <summary>Lower WebView2's memory use while the panel is hidden.</summary>
    public void SetIdle(bool idle)
    {
        if (_web.CoreWebView2 is { } core)
            core.MemoryUsageTargetLevel = idle ? CoreWebView2MemoryUsageTargetLevel.Low : CoreWebView2MemoryUsageTargetLevel.Normal;
    }

    public void FocusInput()
    {
        Win32.ForceForeground(Handle);
        _web.Focus();
        Post(new { type = "focus" });
    }

    /// <summary>Lets the page's header act as a title bar.</summary>
    public void BeginDrag() => Win32.BeginCaptionDrag(Handle);
}
