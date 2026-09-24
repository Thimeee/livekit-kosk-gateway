using System.Text.Json;
using System.Text.Json.Serialization;

namespace VtmKioskShell;

/// <summary>
/// The teller call, as your application sees it.
/// </summary>
/// <remarks>
/// <b>This class and <see cref="TellerCallWindow"/> are the two files to copy into a kiosk
/// application.</b> Everything else in this project is a demonstration of using them.
///
/// Your application keeps its own screen and its own buttons. When a customer asks to speak to
/// someone you call <see cref="StartAsync"/>; a small window appears beside your UI showing the
/// teller. When they are finished you call <see cref="EndAsync"/>. Nothing about the call is
/// drawn by you, and no control for it appears anywhere but in your own UI.
///
/// <code>
/// _call = new TellerCall(ShellConfig.Load());
/// _call.StateChanged += (_, s) => Dispatcher.Invoke(() => ShowStatus(s));
///
/// await _call.StartAsync(owner: this);   // "talk to a teller"
/// await _call.EndAsync();                // "I'm finished"
/// </code>
/// </remarks>
public sealed class TellerCall : IAsyncDisposable
{
    private readonly ShellConfig _config;
    private TellerCallWindow? _window;

    public TellerCall(ShellConfig config) => _config = config;

    /// <summary>Raised whenever the call changes. Arrives on a background thread.</summary>
    public event EventHandler<CallState>? StateChanged;

    /// <summary>The last state reported. Useful for deciding whether to offer a call at all.</summary>
    public CallState State { get; private set; } = new();

    /// <summary>True once the page has loaded and authenticated this kiosk.</summary>
    public bool IsReady => State.Screen is "idle" or "waiting" or "incall";

    /// <summary>
    /// Loads the page and authenticates this kiosk, without showing anything yet.
    /// </summary>
    /// <remarks>
    /// Worth doing as your application starts. The page authenticates the device on load, so
    /// doing it up front means the customer's first press is answered immediately instead of
    /// waiting for a handshake - and a kiosk that has not been enrolled reports that before
    /// anyone tries to use it.
    /// </remarks>
    public async Task PrepareAsync(System.Windows.Window owner)
    {
        if (_window is not null) return;

        _window = new TellerCallWindow(_config);
        _window.CallStateChanged += OnStateChanged;

        await _window.OpenAsync(owner, visible: false);
    }

    /// <summary>
    /// Opens the call window and asks for a teller.
    /// </summary>
    /// <param name="owner">
    /// Your window. The call window is positioned against it and closes with it, so the call
    /// cannot outlive the screen that started it.
    /// </param>
    public async Task StartAsync(System.Windows.Window owner)
    {
        await PrepareAsync(owner);

        _window!.Reveal();
        await _window.SendAsync(new { type = "start" });
    }

    /// <summary>
    /// Ends the call and closes the window. Safe to call when there is no call.
    /// </summary>
    public async Task EndAsync()
    {
        if (_window is null) return;

        await _window.SendAsync(new { type = "end" });

        // Give the page a moment to leave the room cleanly rather than being torn down
        // mid-disconnect, which leaves the session open until the server's own timeout.
        await Task.Delay(400);

        // The window is hidden rather than closed: the page stays loaded and authenticated, so
        // the next customer does not wait through a handshake they should never have seen.
        _window?.Conceal();
    }

    /// <summary>
    /// Tries again after a failure, without restarting your application.
    /// </summary>
    /// <remarks>
    /// Offer this when <see cref="CallState.Screen"/> is <c>error</c> - typically a kiosk that
    /// has not been enrolled, or an API that was not up when the machine started.
    /// </remarks>
    public Task RetryAsync() =>
        _window?.SendAsync(new { type = "retry" }) ?? Task.CompletedTask;

    /// <summary>
    /// The customer stopping the screen share from your own button. The teller is told, exactly
    /// as when the browser's own Stop sharing is used.
    /// </summary>
    public Task StopSharingAsync() =>
        _window?.SendAsync(new { type = "stopShare" }) ?? Task.CompletedTask;

    /// <summary>
    /// Lets audio play. Browsers refuse until a gesture, and a gesture on your button counts —
    /// call this from the click handler, not from a timer.
    /// </summary>
    public Task EnableAudioAsync() =>
        _window?.SendAsync(new { type = "enableAudio" }) ?? Task.CompletedTask;

    private void OnStateChanged(object? sender, CallState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    private async Task CloseWindowAsync()
    {
        var window = _window;
        _window = null;

        if (window is null) return;

        window.CallStateChanged -= OnStateChanged;
        await window.CloseAsync();
    }

    public async ValueTask DisposeAsync() => await CloseWindowAsync();
}

/// <summary>What the call is doing, as reported by the page.</summary>
public sealed record CallState
{
    /// <summary><c>booting</c>, <c>idle</c>, <c>waiting</c>, <c>incall</c> or <c>error</c>.</summary>
    [JsonPropertyName("screen")]
    public string Screen { get; init; } = "booting";

    /// <summary>Already worded for a person to read.</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;

    [JsonPropertyName("tellerName")]
    public string TellerName { get; init; } = string.Empty;

    /// <summary>The call is up but the connection has gone quiet. Say so; do not hide it.</summary>
    [JsonPropertyName("reconnecting")]
    public bool Reconnecting { get; init; }

    /// <summary>Audio is blocked until a gesture. Offer a button that calls EnableAudioAsync.</summary>
    [JsonPropertyName("needsAudioGesture")]
    public bool NeedsAudioGesture { get; init; }

    /// <summary>The teller is viewing this kiosk's screen. Say so in your own UI.</summary>
    [JsonPropertyName("sharing")]
    public bool Sharing { get; init; }

    public bool InCall => Screen == "incall";

    /// <summary>One line fit to put on your own screen.</summary>
    public string Describe() => Screen switch
    {
        _ when Reconnecting => "Reconnecting…",
        "booting" => "Starting…",
        "idle" => "Ready",
        "waiting" => string.IsNullOrWhiteSpace(Status) ? "Waiting for a teller…" : Status,
        "incall" => string.IsNullOrWhiteSpace(TellerName) ? "Connected" : $"Speaking to {TellerName}",
        "error" => string.IsNullOrWhiteSpace(Error) ? "This kiosk is not ready" : Error,
        _ => string.Empty,
    };

    internal static CallState? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<CallState>(json);
        }
        catch
        {
            // Anything the page posts that is not a state message is not our business.
            return null;
        }
    }
}
