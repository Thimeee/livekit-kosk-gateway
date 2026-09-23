using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace VtmKioskShell;

/// <summary>
/// The small window that shows the teller, and nothing else.
/// </summary>
/// <remarks>
/// Built in code rather than XAML so it can be copied into an application as one file.
/// <see cref="TellerCall"/> is the thing to use; this is what it opens.
///
/// It carries no buttons on purpose. On a small kiosk screen the host application owns the
/// display, and a control here would be a second place to look for something the customer
/// already has in front of them.
/// </remarks>
internal sealed class TellerCallWindow : Window
{
    private readonly ShellConfig _config;
    private readonly WebView2 _web = new();
    private readonly TextBlock _splash = new();
    private TaskCompletionSource? _ready;

    internal TellerCallWindow(ShellConfig config)
    {
        _config = config;

        Title = "Teller";
        Width = config.CallWindowWidth;
        Height = config.CallWindowHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0x0b, 0x0d, 0x10));

        // Above the host, but not above everything on the machine: a kiosk should not be able to
        // cover a system dialog someone needs to answer.
        Topmost = false;

        _splash.Text = "Starting…";
        _splash.Foreground = new SolidColorBrush(Color.FromRgb(0x9a, 0xa3, 0xae));
        _splash.FontSize = 13;
        _splash.HorizontalAlignment = HorizontalAlignment.Center;
        _splash.VerticalAlignment = VerticalAlignment.Center;

        _web.Visibility = Visibility.Hidden;
        _web.DefaultBackgroundColor = System.Drawing.Color.Black;

        Content = new Grid { Children = { _splash, _web } };
    }

    /// <summary>Named for the call, not the window - Window.StateChanged is about minimising.</summary>
    internal event EventHandler<CallState>? CallStateChanged;

    /// <summary>
    /// Opens the window beside <paramref name="owner"/> and waits for the page to load.
    /// </summary>
    /// <param name="visible">
    /// False loads the page without showing anything. A kiosk prepares at startup so the
    /// customer's first press is answered at once.
    /// </param>
    internal async Task OpenAsync(Window owner, bool visible = true)
    {
        Owner = owner;
        _ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        PositionAgainst(owner);

        // Follow the host if it moves, so the call does not end up somewhere unrelated.
        owner.LocationChanged += (_, _) => PositionAgainst(owner);
        owner.SizeChanged += (_, _) => PositionAgainst(owner);

        // Shown either way: WebView2 does not load a page in a window that was never shown,
        // and a zero-opacity window is the simplest way to have it load quietly.
        Opacity = visible ? 1 : 0;
        ShowActivated = false;
        Show();

        await StartWebViewAsync();
        await _ready.Task;
    }

    /// <summary>Makes a prepared window visible.</summary>
    internal void Reveal() => Dispatcher.Invoke(() => Opacity = 1);

    /// <summary>Hides it again without unloading the page.</summary>
    internal void Conceal() => Dispatcher.Invoke(() => Opacity = 0);

    /// <summary>Puts the window in the host's corner, inside it, with a small margin.</summary>
    private void PositionAgainst(Window owner)
    {
        if (owner.WindowState == WindowState.Minimized) return;

        const double margin = 16;

        Left = owner.Left + owner.ActualWidth - Width - margin;
        Top = owner.Top + margin;
    }

    private async Task StartWebViewAsync()
    {
        var env = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: _config.UserDataFolder,
            options: new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = string.Join(' ', _config.BrowserArguments()),
            });

        await _web.EnsureCoreWebView2Async(env);

        var core = _web.CoreWebView2;

        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = _config.DevTools;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;

        // Nobody is standing here to answer a permission prompt, and the camera and microphone
        // are the whole point. Anything else is refused.
        core.PermissionRequested += (_, e) =>
        {
            e.State = e.PermissionKind is CoreWebView2PermissionKind.Camera
                                       or CoreWebView2PermissionKind.Microphone
                ? CoreWebView2PermissionState.Allow
                : CoreWebView2PermissionState.Deny;

            e.Handled = true;
        };

        core.WebMessageReceived += OnWebMessage;

        core.NavigationCompleted += (_, args) =>
        {
            if (args.IsSuccess)
            {
                _splash.Visibility = Visibility.Collapsed;
                _web.Visibility = Visibility.Visible;
            }
            else
            {
                _splash.Text = $"Could not load the teller view.\n{args.WebErrorStatus}";
            }

            _ready?.TrySetResult();
        };

        core.NewWindowRequested += (_, args) => args.Handled = true;

        await core.AddScriptToExecuteOnDocumentCreatedAsync(_config.BuildInjectedConfig());

        core.Navigate(ResolveStartUrl(core));
    }

    private string ResolveStartUrl(CoreWebView2 core)
    {
        if (string.IsNullOrWhiteSpace(_config.WebRoot)) return _config.KioskUrl;

        var folder = Path.IsPathRooted(_config.WebRoot)
            ? _config.WebRoot
            : Path.Combine(AppContext.BaseDirectory, _config.WebRoot);

        if (!File.Exists(Path.Combine(folder, "index.html")))
        {
            _splash.Text = $"No index.html in:\n{folder}";
            return "about:blank";
        }

        core.SetVirtualHostNameToFolderMapping(
            _config.VirtualHost, folder, CoreWebView2HostResourceAccessKind.Allow);

        return $"https://{_config.VirtualHost}/index.html";
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json;

        try
        {
            // The page posts a string; TryGetWebMessageAsString throws if it posted an object.
            json = e.TryGetWebMessageAsString();
        }
        catch
        {
            json = e.WebMessageAsJson;
        }

        var state = CallState.Parse(json);
        if (state is not null) CallStateChanged?.Invoke(this, state);
    }

    internal async Task SendAsync(object message)
    {
        if (_web.CoreWebView2 is null) return;

        var json = JsonSerializer.Serialize(message);

        await Dispatcher.InvokeAsync(() => _web.CoreWebView2.PostWebMessageAsString(json));
    }

    internal async Task CloseAsync()
    {
        await Dispatcher.InvokeAsync(() =>
        {
            _web.Dispose();
            Close();
        });
    }
}
