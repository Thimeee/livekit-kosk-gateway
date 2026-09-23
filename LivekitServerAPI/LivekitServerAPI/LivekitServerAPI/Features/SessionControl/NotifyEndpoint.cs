using FastEndpoints;
using LivekitServerAPI.Contracts;
using LivekitServerAPI.Domain.Auth;
using LivekitServerAPI.Infrastructure.LiveKit;

namespace LivekitServerAPI.Features.SessionControl;

/// <summary>
/// Sends a server-originated data message into a session.
/// </summary>
/// <remarks>
/// Fire and forget: use this for state pushes the kiosk simply displays - queue position,
/// "the teller is reviewing your document", a closing countdown. When the result matters
/// (a card read, a print), use a Class 2 command over RPC instead. See PROJECT.md D-010.
/// </remarks>
public class NotifyEndpoint : Endpoint<NotifyRequest, ApiResponse<NotifyResponse>>
{
    private readonly IRoomService _rooms;

    public NotifyEndpoint(IRoomService rooms) => _rooms = rooms;

    public override void Configure()
    {
        Post("/api/sessions/{roomName}/notify");
        Policies(VtmPolicies.Staff);
    }

    public override async Task HandleAsync(NotifyRequest req, CancellationToken ct)
    {
        var room = await _rooms.GetRoomAsync(req.RoomName, ct);
        if (room is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await _rooms.SendDataAsync(req.RoomName, req.Topic, req.Payload, req.ToIdentities, ct);

        await Send.OkAsync(
            ApiResponse<NotifyResponse>.Ok(
                new NotifyResponse(
                    req.Topic,
                    req.ToIdentities?.Length ?? (int)room.NumParticipants,
                    DateTimeOffset.UtcNow)),
            cancellation: ct);
    }
}
