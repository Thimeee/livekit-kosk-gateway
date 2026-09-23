using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace VtmKioskShell;

/// <summary>
/// The kiosk shell: a full-screen window that hosts the kiosk page in WebView2.
/// </summary>
/// <remarks>
/// The page does the work. This exists for the things a browser tab cannot do, and the first of
/// them is already load-bearing: <b>screen sharing</b>. `getDisplayMedia()` normally needs a user
/// gesture and opens a source picker, so the teller's "view their screen" command sits waiting for
/// a click nobody is standing at a kiosk to make. Passing
/// <c>--auto-select-desktop-capture-source</c> at launch is what makes it work unattended, and
/// only the host can pass it. Measured both ways - see PROJECT.md D-028.
///
/// Later it is also where the card reader and the printer live, since a browser cannot reach
/// either.
/// </remarks>
public partial class MainWindow : Window
{
    private readonly ShellConfig _config = ShellConfig.Load();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        KeyDown += OnKeyDown;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await StartWebViewAsync();
        }
        catch (Exception ex)
        {
            Fail($"The kiosk could not start.\n\n{ex.Message}");
        }
    }

    private async Task StartWebViewAsync()
    {
        // The profile directory is deliberately ours rather than the default: it keeps the
        // camera permission we grant below, and it keeps this kiosk's state away from any
        // other Edge or WebView2 instance on the machine.
        var env = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: _config.UserDataFolder,
            options: new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = string.Join(' ', BrowserArguments()),
            });

        await Web.EnsureCoreWebView2Async(env);

        var core = Web.CoreWebView2;

        // A kiosk has no keyboard and nobody to use a context menu or dev tools on it.
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = _config.DevTools;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;

        // Nobody is standing here to answer a permission prompt, and the camera and microphone
        // are the whole point of the device. Anything else is still refused.
        core.PermissionRequested += OnPermissionRequested;

        // A page that fails to load must say so. A blank kiosk tells a customer nothing and a
        // technician less.
        core.NavigationCompleted += (_, args) =>
        {
            if (args.IsSuccess)
            {
                Splash.Visibility = Visibility.Collapsed;
                Web.Visibility = Visibility.Visible;
            }
            else
            {
                Fail($"Could not load the kiosk page at {_config.KioskUrl}\n\n" +
                     $"{args.WebErrorStatus}. Check that the page is being served and that this " +
                     "machine can reach it.");
            }
        };

        // Keep the kiosk on its own page. A stray link must not turn the device into a browser.
        core.NavigationStarting += (_, args) =>
        {
            if (!IsOwnOrigin(args.Uri)) args.Cancel = true;
        };

        core.NewWindowRequested += (_, args) => args.Handled = true;

        // Before the page runs, not after: the kiosk asks who it is during boot, so settings
        // that arrive later would be too late.
        await core.AddScriptToExecuteOnDocumentCreatedAsync(BuildInjectedConfig());

        core.Navigate(ResolveStartUrl(core));
    }

    /// <summary>
    /// Where the page comes from, and the reason there are two answers.
    /// </summary>
    /// <remarks>
    /// <b>Bundled</b> — the built Angular output ships inside this application and WebView2 serves
    /// it from disk under a real origin. No web server is involved, so the kiosk still works when
    /// nothing else on the network does, and installing a kiosk is copying one folder. Updating
    /// the page means updating every kiosk.
    ///
    /// <b>Served</b> — WebView2 points at a URL. One place to update, every kiosk follows. But a
    /// kiosk whose page server is unreachable shows nothing at all.
    ///
    /// The mapping gives a proper http origin rather than <c>file://</c>, which matters: a
    /// file:// page has an opaque origin, so its requests to the API are cross-origin with a
    /// null Origin header and no CORS configuration can allow them.
    /// </remarks>
    private string ResolveStartUrl(CoreWebView2 core)
    {
        if (string.IsNullOrWhiteSpace(_config.WebRoot)) return _config.KioskUrl;

        var folder = Path.IsPathRooted(_config.WebRoot)
            ? _config.WebRoot
            : Path.Combine(AppContext.BaseDirectory, _config.WebRoot);

        if (!File.Exists(Path.Combine(folder, "index.html")))
        {
            // Say which folder was looked in. "It did not start" is not a bug report.
            Fail($"webRoot is set to \"{_config.WebRoot}\" but there is no index.html in:\n\n{folder}");
            return "about:blank";
        }

        core.SetVirtualHostNameToFolderMapping(
            _config.VirtualHost, folder, CoreWebView2HostResourceAccessKind.Allow);

        return $"https://{_config.VirtualHost}/index.html";
    }

    /// <summary>
    /// Hands the page this machine's settings, so one Angular build serves every kiosk.
    /// </summary>
    /// <remarks>
    /// A kiosk id compiled into the bundle would mean a build per machine. The identity of a
    /// device belongs on the device, which is this file - and it is the only place it lives,
    /// rather than being repeated in a second config beside the page.
    ///
    /// The secret travels the same way. Today it sits in <c>kiosk-shell.json</c> because this is
    /// a demo; a real kiosk keeps it under DPAPI or the TPM (PROJECT.md D-021). When that
    /// changes, only this method changes - the page never knows the difference.
    /// </remarks>
    private string BuildInjectedConfig()
    {
        var settings = new Dictionary<string, object?>
        {
            ["kioskId"] = _config.KioskId,
            ["apiBaseUrl"] = _config.ApiBaseUrl,
            ["liveKitUrl"] = _config.LiveKitUrl,
            ["showScreenShareIndicator"] = _config.ShowScreenShareIndicator,
            ["deviceSecret"] = _config.DeviceSecret,
        };

        // Only send what is actually configured, so the page keeps its own default for the rest
        // instead of being handed an empty string that overrides it.
        var present = settings
            .Where(kv => kv.Value is not (null or ""))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var json = System.Text.Json.JsonSerializer.Serialize(present);

        return $"window.__vtmKiosk = {json};";
    }

    private IEnumerable<string> BrowserArguments()
    {
        // The one that matters. Without it the teller's screen-share command opens a picker on
        // this machine and times out. The source name has to match exactly - "Screen 1" does not
        // work, "Entire screen" does. Measured against the installed browser, see D-028.
        yield return $"--auto-select-desktop-capture-source=\"{_config.CaptureSource}\"";

        // The kiosk page plays the teller's audio as soon as it arrives; there is no gesture
        // to wait for, because the customer has already pressed Start by then.
        yield return "--autoplay-policy=no-user-gesture-required";

        if (_config.RemoteDebuggingPort > 0)
        {
            // Lets a test driver attach over CDP and check what the page is really doing, which
            // is the only way to verify this shell short of a person watching the screen. Off
            // unless asked for; a real kiosk must not expose a debugger.
            yield return $"--remote-debugging-port={_config.RemoteDebuggingPort}";
        }

        if (_config.FakeMedia)
        {
            // For running the shell on a machine with no camera - a demo laptop, or CI.
            yield return "--use-fake-device-for-media-stream";
            yield return "--use-fake-ui-for-media-stream";
        }
    }

    private void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        if (!IsOwnOrigin(e.Uri))
        {
            e.State = CoreWebView2PermissionState.Deny;
            return;
        }

        e.State = e.PermissionKind switch
        {
            CoreWebView2PermissionKind.Camera => CoreWebView2PermissionState.Allow,
            CoreWebView2PermissionKind.Microphone => CoreWebView2PermissionState.Allow,
            _ => CoreWebView2PermissionState.Deny,
        };

        e.Handled = true;
    }

    private bool IsOwnOrigin(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return false;

        // When the page is bundled, "our origin" is the virtual host, not the configured URL.
        if (!string.IsNullOrWhiteSpace(_config.WebRoot))
        {
            return parsed.Host.Equals(_config.VirtualHost, StringComparison.OrdinalIgnoreCase);
        }

        return Uri.TryCreate(_config.KioskUrl, UriKind.Absolute, out var home)
               && parsed.Scheme == home.Scheme
               && parsed.Host == home.Host
               && parsed.Port == home.Port;
    }

    /// <summary>
    /// A way out, for the person installing the machine. Deliberately awkward: a customer will
    /// not find it, and it is disabled entirely once <c>Locked</c> is set.
    /// </summary>
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_config.Locked) return;

        var ctrlShift = Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
                        && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (ctrlShift && e.Key == Key.Q) Close();
    }

    private void Fail(string message)
    {
        Splash.Visibility = Visibility.Collapsed;
        Web.Visibility = Visibility.Hidden;
        Error.Text = message;
        Error.Visibility = Visibility.Visible;
    }
}

/// <summary>
/// Shell settings, read from <c>kiosk-shell.json</c> beside the executable.
/// </summary>
/// <remarks>
/// A file rather than a rebuild, because the person installing a kiosk in a branch is not going
/// to have a compiler. Missing file means the defaults, which point at a local dev server.
/// </remarks>
public sealed record ShellConfig
{
    public string KioskUrl { get; init; } = "http://localhost:4201";

    /// <summary>Which kiosk this machine is. The one setting that differs per device.</summary>
    public string KioskId { get; init; } = string.Empty;

    /// <summary>The control API. Empty leaves the page's own default in place.</summary>
    public string ApiBaseUrl { get; init; } = string.Empty;

    /// <summary>The LiveKit ws:// or wss:// URL. Empty leaves the page's default.</summary>
    public string LiveKitUrl { get; init; } = string.Empty;

    /// <summary>
    /// The enrolment secret, handed to the page at boot.
    /// </summary>
    /// <remarks>
    /// Here because this is a demo. A real kiosk receives it once at enrolment and keeps it
    /// where the machine can protect it - DPAPI or the TPM. See PROJECT.md D-021.
    /// </remarks>
    public string DeviceSecret { get; init; } = string.Empty;

    /// <summary>Tell the customer when the teller is viewing their screen. Usually a compliance call.</summary>
    public bool ShowScreenShareIndicator { get; init; }

    /// <summary>
    /// Serve the page from this folder instead of fetching <see cref="KioskUrl"/>.
    /// Relative paths are taken from the executable's folder. Empty means use the URL.
    /// </summary>
    public string WebRoot { get; init; } = string.Empty;

    /// <summary>The origin a bundled page is served under. Must not resolve on the network.</summary>
    public string VirtualHost { get; init; } = "kiosk.vtm";

    /// <summary>Must match a capture source name exactly; "Entire screen" is the usual one.</summary>
    public string CaptureSource { get; init; } = "Entire screen";

    /// <summary>Where WebView2 keeps its profile. Kept out of the default location on purpose.</summary>
    public string UserDataFolder { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "VtmKiosk", "WebView2");

    /// <summary>True on a real kiosk: no way out, no dev tools.</summary>
    public bool Locked { get; init; }

    public bool DevTools { get; init; }

    /// <summary>For a machine with no camera. Never true on a real kiosk.</summary>
    public bool FakeMedia { get; init; }

    /// <summary>Non-zero opens a CDP port for testing. Never set on a real kiosk.</summary>
    public int RemoteDebuggingPort { get; init; }

    public static ShellConfig Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "kiosk-shell.json");

        if (!File.Exists(path)) return new ShellConfig();

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<ShellConfig>(
                       File.ReadAllText(path),
                       new System.Text.Json.JsonSerializerOptions
                       {
                           PropertyNameCaseInsensitive = true,
                       })
                   ?? new ShellConfig();
        }
        catch
        {
            // A malformed file must not stop the kiosk starting. The defaults are sane and a
            // wrong URL announces itself on screen within a second.
            return new ShellConfig();
        }
    }
}
