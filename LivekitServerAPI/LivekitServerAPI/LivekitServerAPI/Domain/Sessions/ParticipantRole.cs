namespace LivekitServerAPI.Domain.Sessions;

/// <summary>
/// Who a participant is in a VTM session. Determines the grants their token carries.
/// </summary>
public enum ParticipantRole
{
    /// <summary>The customer-facing kiosk. Publishes camera, microphone and screen. No admin rights.</summary>
    Kiosk,

    /// <summary>The agent handling the session. May moderate this one room.</summary>
    Teller,

    /// <summary>Observes without appearing in the room or publishing anything.</summary>
    Supervisor,
}
