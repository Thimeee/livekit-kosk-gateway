namespace LivekitServerAPI.Domain.Sessions;

public enum SubmissionStatus
{
    Submitted,
    Confirmed,
    Failed,
}

/// <summary>
/// The link from a session to something the core banking system did.
/// </summary>
/// <remarks>
/// <b>No amounts, no account numbers, no customer identifiers</b> - the core system's reference
/// and nothing else. This lets a session timeline show <i>that</i> a transfer was submitted
/// without this API knowing <i>what</i> it was. See docs/05-data-model.md.
/// </remarks>
public class SessionSubmission
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string TellerId { get; set; } = string.Empty;
    public DateTimeOffset SubmittedAt { get; set; }

    /// <summary>"transfer", "address-update", and so on.</summary>
    public string OperationType { get; set; } = string.Empty;

    /// <summary>The core system's transaction id. The whole of the relationship.</summary>
    public string CoreReference { get; set; } = string.Empty;

    public SubmissionStatus Status { get; set; } = SubmissionStatus.Submitted;

    public Session? Session { get; set; }
}
