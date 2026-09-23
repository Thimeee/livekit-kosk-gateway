using FastEndpoints;
using LivekitServerAPI.Contracts;
using LivekitServerAPI.Domain.Auth;
using LivekitServerAPI.Domain.Sessions;
using LivekitServerAPI.Infrastructure.LiveKit;
using LivekitServerAPI.Infrastructure.Persistence;
using LivekitServerAPI.Infrastructure.Realtime;

namespace LivekitServerAPI.Features.Sessions;

/// <summary>
/// Ends a session: destroys the room and disconnects everyone in it.
/// </summary>
public class EndSessionEndpoint : Endpoint<EndSessionRequest, ApiResponse<EndSessionResponse>>
{
    private readonly IRoomService _rooms;
    private readonly ISessionStore _store;
    private readonly IQueueNotifier _notifier;

    public EndSessionEndpoint(IRoomService rooms, ISessionStore store, IQueueNotifier notifier)
    {
        _rooms = rooms;
        _store = store;
        _notifier = notifier;
    }

    public override void Configure()
    {
        Delete("/api/sessions/{roomName}");
        Policies(VtmPolicies.Staff);
    }

    public override async Task HandleAsync(EndSessionRequest req, CancellationToken ct)
    {
        var room = await _rooms.GetRoomAsync(req.RoomName, ct);
        if (room is null)
        {
            // LiveKit's DeleteRoom is happy to delete nothing, but a 404 is more honest
            // to a caller that thinks it is ending a live session.
            await Send.NotFoundAsync(ct);
            return;
        }

        await _rooms.DeleteRoomAsync(req.RoomName, ct);

        // Deleting the room destroys everything LiveKit knew. Record the outcome first-class:
        // a session with no teller never got served, so it was abandoned, not completed.
        var endedAt = DateTimeOffset.UtcNow;
        var persisted = await _store.FindByRoomAsync(req.RoomName, ct);
        var status = persisted?.AcceptedAt is null ? SessionStatus.Abandoned : SessionStatus.Ended;
        var reason = status == SessionStatus.Abandoned
            ? SessionEndReason.CustomerLeft
            : SessionEndReason.TellerEnded;

        await _store.SetStatusAsync(req.RoomName, status, reason, endedAt, ct);

        // Drop it from every teller's list, including anyone who never saw it accepted.
        if (persisted is not null)
        {
            await _notifier.SessionEndedAsync(persisted.BranchId, req.RoomName, ct);
        }

        Logger.LogInformation(
            "Session {RoomName} ended with {ParticipantCount} participants still connected",
            req.RoomName, room.NumParticipants);

        await Send.OkAsync(
            ApiResponse<EndSessionResponse>.Ok(
                new EndSessionResponse(req.RoomName, DateTimeOffset.UtcNow),
                "Session ended."),
            cancellation: ct);
    }
}
