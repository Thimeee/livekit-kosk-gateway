namespace LivekitServerAPI.Features.SessionControl;

/// <summary>Route-bound identifiers shared by the participant control endpoints.</summary>
public abstract record ParticipantRouteRequest
{
    public string RoomName { get; set; } = string.Empty;
    public string Identity { get; set; } = string.Empty;
}

/// <param name="TrackSid">From the participant's track list. Omit to mute every track they publish.</param>
public record MuteParticipantRequest(bool Muted, string? TrackSid = null) : ParticipantRouteRequest;

public record MuteParticipantResponse(string Identity, bool Muted, int TracksAffected);

public record RemoveParticipantRequest : ParticipantRouteRequest;

public record RemoveParticipantResponse(string Identity, DateTimeOffset RemovedAt);

public record UpdatePermissionsRequest(bool CanPublish, bool CanPublishData) : ParticipantRouteRequest;

public record UpdatePermissionsResponse(string Identity, bool CanPublish, bool CanPublishData);

public record NotifyRequest(string Topic, string Payload, string[]? ToIdentities = null)
{
    public string RoomName { get; set; } = string.Empty;
}

public record NotifyResponse(string Topic, int Recipients, DateTimeOffset SentAt);

// ── Transfer / monitor ──────────────────────────────────────────────────
// Both keep the customer in the room they are already in. See D-018.

public record TransferSessionRequest(string ToTellerId)
{
    public string RoomName { get; set; } = string.Empty;
}

/// <param name="FromTellerIdentity">Null when the session was unattended at the time of transfer.</param>
public record TransferSessionResponse(
    string RoomName,
    string? FromTellerIdentity,
    string ToTellerIdentity,
    string TellerToken,
    DateTimeOffset TokenExpiresAt);

public record MonitorSessionRequest(string SupervisorId)
{
    public string RoomName { get; set; } = string.Empty;
}

public record MonitorSessionResponse(
    string RoomName,
    string SupervisorIdentity,
    string SupervisorToken,
    DateTimeOffset TokenExpiresAt);
