namespace LivekitServerAPI.Domain.Sessions;

/// <summary>
/// Where a VTM session is in its life. The first three are also written into the room metadata,
/// so a change broadcasts <c>RoomMetadataChanged</c> to every participant.
/// </summary>
public enum SessionStatus
{
    /// <summary>Customer is at the kiosk; no teller has accepted yet.</summary>
    Waiting,

    /// <summary>A teller has accepted and the call is live.</summary>
    Active,

    /// <summary>Wrapping up - distinct from deleting the room so clients can show a closing state.</summary>
    Ending,

    /// <summary>Finished. Set by the system when the room is deleted, never by a caller.</summary>
    Ended,

    /// <summary>The customer left before any teller accepted. Set by the system.</summary>
    Abandoned,
}

/// <summary>Why a session finished. Drives completion and abandonment reporting.</summary>
public enum SessionEndReason
{
    Completed,
    CustomerLeft,
    TellerEnded,
    Timeout,
    Error,
}
