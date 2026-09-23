namespace LivekitServerAPI.Domain.Operations;

/// <summary>
/// Idempotency record for LiveKit webhooks.
/// </summary>
/// <remarks>
/// <b>LiveKit redelivers.</b> Without this, a retried <c>room_finished</c> would end the same
/// session twice and corrupt its recorded duration. Check this table before processing.
/// </remarks>
public class WebhookDelivery
{
    /// <summary>LiveKit's own event id.</summary>
    public string EventId { get; set; } = string.Empty;

    public DateTimeOffset ReceivedAt { get; set; }
    public string EventType { get; set; } = string.Empty;
}
