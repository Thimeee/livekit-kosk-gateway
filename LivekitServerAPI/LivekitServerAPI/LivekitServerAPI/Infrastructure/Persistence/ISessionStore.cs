using LivekitServerAPI.Domain.Sessions;

namespace LivekitServerAPI.Infrastructure.Persistence;

/// <summary>
/// Durable session records. LiveKit rooms are ephemeral - once a room is deleted, everything
/// LiveKit knew about the session is gone. This is what survives it.
/// </summary>
public interface ISessionStore
{
    Task<Session> CreateAsync(
        string roomName, string? roomSid, string kioskId, string branchId,
        DateTimeOffset createdAt, CancellationToken ct = default);

    Task<Session?> FindByRoomAsync(string roomName, CancellationToken ct = default);

    Task<IReadOnlyList<Session>> ListActiveAsync(CancellationToken ct = default);

    /// <summary>
    /// Moves a session to a new status, stamping whichever timestamps that transition implies
    /// and deriving <c>WaitSeconds</c> / <c>DurationSeconds</c>.
    /// </summary>
    Task<Session?> SetStatusAsync(
        string roomName, SessionStatus status, SessionEndReason? endReason,
        DateTimeOffset at, CancellationToken ct = default);

    /// <summary>Appends to the session timeline. Never updates, never deletes.</summary>
    Task RecordEventAsync(
        Guid sessionId, EventSource source, string eventType,
        string? actorId = null, string? payload = null,
        DateTimeOffset? occurredAt = null, CancellationToken ct = default);
}
