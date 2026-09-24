using System.ComponentModel.DataAnnotations;

namespace LivekitServerAPI.Infrastructure.LiveKit;

/// <summary>
/// Bound from the "LiveKit" section of appsettings.json and validated at startup,
/// so a bad or missing value fails the boot rather than the first request.
/// </summary>
public sealed class LiveKitOptions
{
    public const string SectionName = "LiveKit";

    /// <summary>
    /// The LiveKit server's <b>HTTP</b> URL, e.g. <c>http://localhost:7880</c>.
    /// Not the <c>ws://</c> URL - that one is for clients.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string ServerUrl { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Must be at least 32 bytes - the SDK throws otherwise.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string ApiSecret { get; set; } = string.Empty;

    /// <summary>
    /// How long an issued token stays valid for joining. A VTM session is minutes,
    /// so this is deliberately far below the SDK's 6 hour default.
    /// </summary>
    public TimeSpan TokenTtl { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Hard cap on participants in a session room. Three, not two: kiosk + teller +
    /// one <b>hidden</b> supervisor who may need to observe. Setting this to 2 would
    /// lock supervisors out. It is a cheap guard - even a leaked token cannot let a
    /// fourth party into a session.
    /// </summary>
    public uint MaxSessionParticipants { get; set; } = 3;

    /// <summary>
    /// How long a room with nobody in it survives. Must comfortably exceed the time a
    /// customer waits in the queue for a teller to accept.
    /// </summary>
    public TimeSpan SessionEmptyTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long the room lingers after the last participant leaves.</summary>
    /// <remarks>
    /// This is the window in which a customer and teller who have <b>both</b> dropped out can
    /// still come back to the same call. Once it passes LiveKit deletes the room, the stale
    /// session sweep retires the session, and a rejoin gets 404. It was 20 seconds, which a
    /// kiosk restarting after a power blip cannot meet; two minutes is the agreed window
    /// (PROJECT.md D-034).
    /// </remarks>
    public TimeSpan SessionDepartureTimeout { get; set; } = TimeSpan.FromMinutes(2);
}
