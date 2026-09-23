using System.IO;
using System.Text.Json;

namespace VtmKioskShell;

/// <summary>
/// Kiosk settings, read from <c>kiosk-shell.json</c> beside the executable.
/// </summary>
/// <remarks>
/// A file rather than a rebuild: whoever installs a kiosk in a branch does not have a compiler.
/// A missing or malformed file means the defaults — a kiosk must still start and say what is
/// wrong, rather than not start at all.
/// </remarks>
public sealed record ShellConfig
{
    // ── Which kiosk this is ──────────────────────────────────────────────

    /// <summary>The one setting that genuinely differs per device.</summary>
    public string KioskId { get; init; } = string.Empty;

    /// <summary>The control API. Empty leaves the page's own default in place.</summary>
    public string ApiBaseUrl { get; init; } = string.Empty;

    /// <summary>The LiveKit ws:// or wss:// URL. Empty leaves the page's default.</summary>
    public string LiveKitUrl { get; init; } = string.Empty;

    /// <summary>
    /// The enrolment secret, handed to the page at boot.
    /// </summary>
    /// <remarks>
    /// Here because this is a demo. A real kiosk receives it once at enrolment and keeps it where
    /// the machine can protect it — DPAPI or the TPM. See PROJECT.md D-021. When that changes,
    /// only <see cref="BuildInjectedConfig"/> changes; the page never knows the difference.
    /// </remarks>
    public string DeviceSecret { get; init; } = string.Empty;

    /// <summary>Tell the customer when the teller is viewing their screen. Usually a compliance call.</summary>
    public bool ShowScreenShareIndicator { get; init; }

    // ── Where the page comes from ────────────────────────────────────────

    /// <summary>
    /// Serve the page from this folder instead of fetching <see cref="KioskUrl"/>. Relative
    /// paths are taken from the executable's folder. Empty means use the URL.
    /// </summary>
    public string WebRoot { get; init; } = "webroot";

    /// <summary>The origin a bundled page is served under. Must not resolve on the network.</summary>
    public string VirtualHost { get; init; } = "kiosk.vtm";

    /// <summary>Used only when <see cref="WebRoot"/> is empty.</summary>
    public string KioskUrl { get; init; } = "http://localhost:4201";

    // ── The call window ──────────────────────────────────────────────────

    /// <summary>Small on purpose: it sits beside the host's UI, not over it.</summary>
    public double CallWindowWidth { get; init; } = 320;

    public double CallWindowHeight { get; init; } = 240;

    // ── The host browser ─────────────────────────────────────────────────

    /// <summary>Must match a capture source name exactly; "Entire screen" is the usual one.</summary>
    public string CaptureSource { get; init; } = "Entire screen";

    /// <summary>Where WebView2 keeps its profile. Deliberately not the shared default.</summary>
    public string UserDataFolder { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "VtmKiosk", "WebView2");

    public bool DevTools { get; init; }

    /// <summary>For a machine with no camera. Never true on a real kiosk.</summary>
    public bool FakeMedia { get; init; }

    /// <summary>Non-zero opens a CDP port for testing. Never set on a real kiosk.</summary>
    public int RemoteDebuggingPort { get; init; }

    /// <summary>
    /// The launch arguments WebView2 needs, and the reason a host process is required at all.
    /// </summary>
    public IEnumerable<string> BrowserArguments()
    {
        // The one that matters. Without it the teller's screen-share command opens a picker on
        // this machine and times out - there is nobody at a kiosk to answer one. The source name
        // has to match exactly: "Screen 1" does not work, "Entire screen" does. Measured against
        // the installed browser, see PROJECT.md D-028.
        yield return $"--auto-select-desktop-capture-source=\"{CaptureSource}\"";

        // The teller's audio plays as soon as it arrives; the customer already pressed a button
        // in the host application by then.
        yield return "--autoplay-policy=no-user-gesture-required";

        if (RemoteDebuggingPort > 0)
        {
            yield return $"--remote-debugging-port={RemoteDebuggingPort}";
        }

        if (FakeMedia)
        {
            yield return "--use-fake-device-for-media-stream";
            yield return "--use-fake-ui-for-media-stream";
        }
    }

    /// <summary>
    /// Hands the page this machine's settings, so one Angular build serves every kiosk.
    /// </summary>
    /// <remarks>
    /// A kiosk id compiled into the bundle would mean a build per machine. The identity of a
    /// device belongs on the device, which is this file — and only here, rather than repeated in
    /// a second config beside the page.
    /// </remarks>
    public string BuildInjectedConfig()
    {
        var settings = new Dictionary<string, object?>
        {
            ["kioskId"] = KioskId,
            ["apiBaseUrl"] = ApiBaseUrl,
            ["liveKitUrl"] = LiveKitUrl,
            ["showScreenShareIndicator"] = ShowScreenShareIndicator,
            ["deviceSecret"] = DeviceSecret,
        };

        // Only send what is configured, so the page keeps its own default for the rest instead
        // of being handed an empty string that overrides it.
        var present = settings
            .Where(kv => kv.Value is not (null or ""))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        return $"window.__vtmKiosk = {JsonSerializer.Serialize(present)};";
    }

    public static ShellConfig Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "kiosk-shell.json");

        if (!File.Exists(path)) return new ShellConfig();

        try
        {
            return JsonSerializer.Deserialize<ShellConfig>(
                       File.ReadAllText(path),
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new ShellConfig();
        }
        catch
        {
            return new ShellConfig();
        }
    }
}
