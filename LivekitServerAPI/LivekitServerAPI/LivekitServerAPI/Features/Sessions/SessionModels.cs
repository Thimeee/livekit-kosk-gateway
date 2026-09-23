namespace LivekitServerAPI.Features.Sessions;

// ── Create ──────────────────────────────────────────────────────────────

/// <param name="KioskId">Which kiosk the customer is standing at. Must be registered and Active.</param>
/// <remarks>The branch is taken from the kiosk record, not from the caller.</remarks>
public record CreateSessionRequest(string KioskId);

/// <summary>
/// Everything the kiosk needs to join, in one round trip: the room it should connect to
/// and the token to connect with.
/// </summary>
public record CreateSessionResponse(
    Guid SessionId,
    string RoomName,
    string RoomSid,
    string BranchId,
    string Status,
    string KioskToken,
    string KioskIdentity,
    DateTimeOffset TokenExpiresAt,
    DateTimeOffset CreatedAt);

// ── Read ────────────────────────────────────────────────────────────────

public record SessionSummary(
    string RoomName,
    string RoomSid,
    string Status,
    string? KioskId,
    string? BranchId,
    uint NumParticipants,
    uint NumPublishers,
    bool ActiveRecording,
    DateTimeOffset CreatedAt);

public record SessionListResponse(int Count, IReadOnlyList<SessionSummary> Sessions);

public record ParticipantSummary(
    string Identity,
    string Name,
    string Sid,
    string Role,
    string State,
    bool IsPublisher,
    int TrackCount,
    DateTimeOffset JoinedAt);

public record SessionDetailResponse(
    SessionSummary Session,
    IReadOnlyList<ParticipantSummary> Participants);

// ── Update / delete ─────────────────────────────────────────────────────

/// <param name="Status">"waiting", "active" or "ending" - case-insensitive.</param>
public record UpdateSessionStatusRequest(string Status)
{
    /// <summary>Bound from the route, not the body.</summary>
    public string RoomName { get; set; } = string.Empty;
}

public record EndSessionRequest
{
    public string RoomName { get; set; } = string.Empty;
}

public record EndSessionResponse(string RoomName, DateTimeOffset EndedAt);

/// <summary>Ask for another token for a session the caller is already part of.</summary>
public record RejoinSessionRequest
{
    public string RoomName { get; init; } = string.Empty;
}

/// <param name="Status">So a client that was away can tell whether a teller has since joined.</param>
public record RejoinSessionResponse(string RoomName, string Token, string Status);
