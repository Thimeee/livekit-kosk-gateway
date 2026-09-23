using LivekitServerAPI.Domain.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace LivekitServerAPI.Infrastructure.Realtime;

/// <summary>
/// Sends queue events to connected tellers. Endpoints depend on this rather than on
/// <see cref="IHubContext{THub,T}"/> directly, so the transport can change without touching them.
/// </summary>
public interface IQueueNotifier
{
    Task CustomerWaitingAsync(Session session, string kioskName, CancellationToken ct = default);

    Task SessionTakenAsync(string branchId, string roomName, string tellerId, CancellationToken ct = default);

    Task SessionEndedAsync(string branchId, string roomName, CancellationToken ct = default);
}

public sealed class SignalRQueueNotifier : IQueueNotifier
{
    private readonly IHubContext<QueueHub, IQueueClient> _hub;
    private readonly ILogger<SignalRQueueNotifier> _logger;

    public SignalRQueueNotifier(
        IHubContext<QueueHub, IQueueClient> hub, ILogger<SignalRQueueNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task CustomerWaitingAsync(
        Session session, string kioskName, CancellationToken ct = default)
    {
        await _hub.Clients
            .Group(QueueHub.BranchGroup(session.BranchId))
            .SessionWaiting(new QueueEntryNotification(
                session.SessionId, session.RoomName, session.KioskId, kioskName,
                session.BranchId, session.CreatedAt));

        _logger.LogInformation(
            "Rang branch {BranchId} for session {RoomName} from kiosk {KioskId}",
            session.BranchId, session.RoomName, session.KioskId);
    }

    public Task SessionTakenAsync(
        string branchId, string roomName, string tellerId, CancellationToken ct = default) =>
        _hub.Clients.Group(QueueHub.BranchGroup(branchId)).SessionTaken(roomName, tellerId);

    public Task SessionEndedAsync(
        string branchId, string roomName, CancellationToken ct = default) =>
        _hub.Clients.Group(QueueHub.BranchGroup(branchId)).SessionEnded(roomName);
}
