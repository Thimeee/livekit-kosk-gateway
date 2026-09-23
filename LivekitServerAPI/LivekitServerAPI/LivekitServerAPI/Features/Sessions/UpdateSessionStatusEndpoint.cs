using FastEndpoints;
using LivekitServerAPI.Contracts;
using LivekitServerAPI.Domain.Auth;
using LivekitServerAPI.Domain.Sessions;
using LivekitServerAPI.Infrastructure.LiveKit;
using LivekitServerAPI.Infrastructure.Persistence;

namespace LivekitServerAPI.Features.Sessions;

/// <summary>
/// Moves a session between waiting, active and ending.
/// </summary>
/// <remarks>
/// Writing the status into room metadata broadcasts <c>RoomMetadataChanged</c> to every
/// participant, so the kiosk and teller UIs react without polling this API.
/// </remarks>
public class UpdateSessionStatusEndpoint
    : Endpoint<UpdateSessionStatusRequest, ApiResponse<SessionSummary>>
{
    private readonly IRoomService _rooms;
    private readonly ISessionStore _store;

    public UpdateSessionStatusEndpoint(IRoomService rooms, ISessionStore store)
    {
        _rooms = rooms;
        _store = store;
    }

    public override void Configure()
    {
        Patch("/api/sessions/{roomName}/status");
        Policies(VtmPolicies.Staff);
    }

    public override async Task HandleAsync(UpdateSessionStatusRequest req, CancellationToken ct)
    {
        var room = await _rooms.GetRoomAsync(req.RoomName, ct);
        if (room is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var status = Enum.Parse<SessionStatus>(req.Status, ignoreCase: true);

        // Keep everything else in the metadata; only the status moves.
        var current = SessionMapping.ToSummary(room);
        var updated = new SessionMetadata(
            Status: status.ToString(),
            KioskId: current.KioskId ?? string.Empty,
            BranchId: current.BranchId,
            CreatedAt: current.CreatedAt);

        await _rooms.UpdateSessionMetadataAsync(req.RoomName, updated, ct);

        // The room metadata is what live clients react to; this row is what survives the room.
        await _store.SetStatusAsync(req.RoomName, status, endReason: null, DateTimeOffset.UtcNow, ct);

        Logger.LogInformation(
            "Session {RoomName} moved {FromStatus} -> {ToStatus}",
            req.RoomName, current.Status, status.ToString());

        await Send.OkAsync(
            ApiResponse<SessionSummary>.Ok(current with { Status = status.ToString() }),
            cancellation: ct);
    }
}
