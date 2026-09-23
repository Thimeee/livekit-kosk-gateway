namespace LivekitServerAPI.Domain.Sessions;

public enum CommandStatus
{
    Pending,
    Succeeded,
    Failed,
    TimedOut,
}

/// <summary>
/// A Class 2 command - one with a real-world consequence: a card read, a print, a signature
/// capture.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="SessionEvent"/> because these have a request/response lifecycle and
/// carry legal weight: they are recorded whether or not a submission follows. Because they go out
/// over <c>Twirp.PerformRpc</c> the kiosk's actual result comes back, so the record can say
/// <i>succeeded</i> rather than merely <i>attempted</i>. See docs/PROJECT.md D-010 and D-011.
/// </remarks>
public class SessionCommand
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }

    /// <summary>"card-read", "print-receipt", "capture-signature".</summary>
    public string Command { get; set; } = string.Empty;

    public string RequestedBy { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }
    public CommandStatus Status { get; set; } = CommandStatus.Pending;
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ResultPayload { get; set; }
    public string? ErrorMessage { get; set; }

    public Session? Session { get; set; }
}
