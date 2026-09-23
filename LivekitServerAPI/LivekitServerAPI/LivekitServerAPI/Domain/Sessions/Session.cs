using LivekitServerAPI.Domain.Devices;
using LivekitServerAPI.Domain.Staff;

namespace LivekitServerAPI.Domain.Sessions;

/// <summary>
/// A VTM session. The central record - everything else in the schema hangs off it.
/// </summary>
public class Session
{
    public Guid SessionId { get; set; }

    /// <summary>
    /// The LiveKit room name, generated server-side. Unique, and the join key that links a
    /// session to whatever the core banking system recorded for it.
    /// </summary>
    public string RoomName { get; set; } = string.Empty;

    /// <summary>LiveKit's own room id (<c>RM_...</c>).</summary>
    public string? RoomSid { get; set; }

    public string KioskId { get; set; } = string.Empty;

    /// <summary>Denormalised from the kiosk so branch reporting does not need a join.</summary>
    public string BranchId { get; set; } = string.Empty;

    /// <summary>Null until a teller accepts.</summary>
    public string? TellerId { get; set; }

    public SessionStatus Status { get; set; } = SessionStatus.Waiting;
    public SessionEndReason? EndReason { get; set; }

    /// <summary>Customer arrived at the kiosk.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }

    /// <summary>
    /// First moment both parties were actually in the room. Comes from webhooks, never from a
    /// client claiming it joined.
    /// </summary>
    public DateTimeOffset? ConnectedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Derived and stored so reports do not recompute them across the whole table.</summary>
    public int? WaitSeconds { get; set; }

    public int? DurationSeconds { get; set; }

    public Kiosk? Kiosk { get; set; }
    public Teller? Teller { get; set; }
    public ICollection<SessionEvent> Events { get; set; } = [];
    public ICollection<SessionCommand> Commands { get; set; } = [];
    public ICollection<SessionSubmission> Submissions { get; set; } = [];
}
