using LivekitServerAPI.Domain.Sessions;

namespace LivekitServerAPI.Domain.Operations;

public enum RecordingStatus
{
    Starting,
    Active,
    Ended,
    Failed,
}

/// <summary>
/// Metadata for an egress recording of a session. The media itself lives wherever egress was told
/// to put it; this row only points at it.
/// </summary>
/// <remarks>
/// Unused until <c>livekit-egress</c> and Redis are deployed. Modelled now so that turning
/// recording on later is a feature, not a migration.
/// </remarks>
public class Recording
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }

    /// <summary>The egress id LiveKit returns, needed to stop it again.</summary>
    public string EgressId { get; set; } = string.Empty;

    public RecordingStatus Status { get; set; } = RecordingStatus.Starting;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string? FileUri { get; set; }
    public long? SizeBytes { get; set; }

    public Session? Session { get; set; }
}
