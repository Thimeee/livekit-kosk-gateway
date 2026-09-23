using LivekitServerAPI.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace LivekitServerAPI.Infrastructure.Persistence;

/// <inheritdoc cref="ISessionStore"/>
public sealed class SessionStore : ISessionStore
{
    private readonly VtmDbContext _db;
    private readonly ILogger<SessionStore> _logger;

    public SessionStore(VtmDbContext db, ILogger<SessionStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Session> CreateAsync(
        string roomName, string? roomSid, string kioskId, string branchId,
        DateTimeOffset createdAt, CancellationToken ct = default)
    {
        var session = new Session
        {
            SessionId = Guid.NewGuid(),
            RoomName = roomName,
            RoomSid = roomSid,
            KioskId = kioskId,
            BranchId = branchId,
            Status = SessionStatus.Waiting,
            CreatedAt = createdAt,
        };

        _db.Sessions.Add(session);
        _db.SessionEvents.Add(new SessionEvent
        {
            SessionId = session.SessionId,
            OccurredAt = createdAt,
            Source = EventSource.Api,
            EventType = "session.created",
            ActorId = kioskId,
        });

        // One SaveChanges: the session and its first timeline entry commit together or not at all.
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Persisted session {SessionId} for room {RoomName}", session.SessionId, roomName);

        return session;
    }

    public Task<Session?> FindByRoomAsync(string roomName, CancellationToken ct = default) =>
        _db.Sessions.FirstOrDefaultAsync(s => s.RoomName == roomName, ct);

    public async Task<IReadOnlyList<Session>> ListActiveAsync(CancellationToken ct = default) =>
        await _db.Sessions
            .Where(s => s.Status == SessionStatus.Waiting
                     || s.Status == SessionStatus.Active
                     || s.Status == SessionStatus.Ending)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(ct);

    public async Task<Session?> SetStatusAsync(
        string roomName, SessionStatus status, SessionEndReason? endReason,
        DateTimeOffset at, CancellationToken ct = default)
    {
        var session = await _db.Sessions.FirstOrDefaultAsync(s => s.RoomName == roomName, ct);
        if (session is null)
        {
            return null;
        }

        var previous = session.Status;
        session.Status = status;

        switch (status)
        {
            case SessionStatus.Active when session.AcceptedAt is null:
                session.AcceptedAt = at;
                session.WaitSeconds = (int)(at - session.CreatedAt).TotalSeconds;
                break;

            case SessionStatus.Ended:
            case SessionStatus.Abandoned:
                session.EndedAt = at;
                session.EndReason = endReason;
                // Duration runs from when a teller picked it up, not from when the customer
                // arrived - otherwise queue time would be counted as handling time.
                var from = session.AcceptedAt ?? session.CreatedAt;
                session.DurationSeconds = (int)(at - from).TotalSeconds;
                break;
        }

        _db.SessionEvents.Add(new SessionEvent
        {
            SessionId = session.SessionId,
            OccurredAt = at,
            Source = EventSource.Api,
            EventType = "session.status-changed",
            Payload = $$"""{"from":"{{previous}}","to":"{{status}}"}""",
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Session {RoomName} persisted {FromStatus} -> {ToStatus}", roomName, previous, status);

        return session;
    }

    public async Task RecordEventAsync(
        Guid sessionId, EventSource source, string eventType,
        string? actorId = null, string? payload = null,
        DateTimeOffset? occurredAt = null, CancellationToken ct = default)
    {
        _db.SessionEvents.Add(new SessionEvent
        {
            SessionId = sessionId,
            OccurredAt = occurredAt ?? DateTimeOffset.UtcNow,
            Source = source,
            EventType = eventType,
            ActorId = actorId,
            Payload = payload,
        });

        await _db.SaveChangesAsync(ct);
    }
}
