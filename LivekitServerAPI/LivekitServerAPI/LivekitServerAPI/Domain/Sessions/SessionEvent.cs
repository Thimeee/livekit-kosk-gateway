namespace LivekitServerAPI.Domain.Sessions;

public enum EventSource
{
    Api,
    Webhook,
    Kiosk,
    Teller,
}

/// <summary>
/// Append-only timeline for a session. Webhook handlers write here, and so does the API for
/// anything worth reconstructing later. Never updated, never deleted.
/// </summary>
public class SessionEvent
{
    public long Id { get; set; }
    public Guid SessionId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public EventSource Source { get; set; }

    /// <summary>Dotted name - "session.created", "participant.joined", "track.published".</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Teller or kiosk id, when the event had an actor.</summary>
    public string? ActorId { get; set; }

    /// <summary>Small JSON. No PII - this table is read during support and audit.</summary>
    public string? Payload { get; set; }

    public Session? Session { get; set; }
}
