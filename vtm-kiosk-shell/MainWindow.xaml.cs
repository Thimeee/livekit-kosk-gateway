using System.Windows;

namespace VtmKioskShell;

/// <summary>
/// A stand-in for the bank's own kiosk application, showing how to use
/// <see cref="TellerCall"/>.
/// </summary>
/// <remarks>
/// The point of this window is how little there is to it. The kiosk application keeps its own
/// screen, its own buttons and its own look; the teller appears in a small window beside it, and
/// this class only has to do three things:
///
/// <list type="number">
///   <item>Create a <see cref="TellerCall"/> and listen to <see cref="TellerCall.StateChanged"/>.</item>
///   <item>Call <see cref="TellerCall.StartAsync"/> when the customer asks to speak to someone.</item>
///   <item>Call <see cref="TellerCall.EndAsync"/> when they are finished.</item>
/// </list>
///
/// Copy <c>TellerCall.cs</c>, <c>TellerCallWindow.cs</c> and <c>ShellConfig.cs</c> into your
/// application and the same three steps apply there.
/// </remarks>
public partial class MainWindow : Window
{
    private readonly TellerCall _call = new(ShellConfig.Load());

    public MainWindow()
    {
        InitializeComponent();

        // The page reports from a background thread, so everything here goes back to the UI one.
        _call.StateChanged += (_, state) => Dispatcher.Invoke(() => Render(state));

        Loaded += async (_, _) => await PrepareAsync();
        Closed += async (_, _) => await _call.DisposeAsync();
    }

    /// <summary>
    /// Opens the call window early and leaves it hidden until there is something to show.
    /// </summary>
    /// <remarks>
    /// The page authenticates this kiosk as a device on load, which takes a moment. Doing it now
    /// means the customer's first press is answered immediately rather than waiting for a
    /// handshake, and it means a kiosk that has not been enrolled says so before anyone tries.
    /// </remarks>
    private async Task PrepareAsync()
    {
        try
        {
            await _call.PrepareAsync(this);
        }
        catch (Exception ex)
        {
            Status.Text = $"The teller service could not start. {ex.Message}";
        }
    }

    private async void OnTalkClick(object sender, RoutedEventArgs e)
    {
        TalkButton.IsEnabled = false;
        await _call.StartAsync(this);
    }

    private async void OnFinishClick(object sender, RoutedEventArgs e)
    {
        FinishButton.IsEnabled = false;
        await _call.EndAsync();
        FinishButton.IsEnabled = true;
    }

    // Browsers refuse to play audio until a gesture. A click on this button is one, and
    // forwarding it is enough.
    private async void OnAudioClick(object sender, RoutedEventArgs e) =>
        await _call.EnableAudioAsync();

    private void Render(CallState state)
    {
        Status.Text = state.Describe();

        var busy = state.Screen is "waiting" or "incall";

        TalkButton.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        TalkButton.IsEnabled = state.Screen == "idle";

        FinishButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        AudioButton.Visibility = state.NeedsAudioGesture ? Visibility.Visible : Visibility.Collapsed;
    }
}
