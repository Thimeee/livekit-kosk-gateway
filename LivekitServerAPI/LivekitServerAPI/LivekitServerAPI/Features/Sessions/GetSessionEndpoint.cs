using FastEndpoints;
using LivekitServerAPI.Contracts;
using LivekitServerAPI.Domain.Auth;
using LivekitServerAPI.Infrastructure.LiveKit;

namespace LivekitServerAPI.Features.Sessions;

public record GetSessionRequest
{
    public string RoomName { get; set; } = string.Empty;
}

/// <summary>
/// One session with everyone currently in it.
/// </summary>
public class GetSessionEndpoint : Endpoint<GetSessionRequest, ApiResponse<SessionDetailResponse>>
{
    private readonly IRoomService _rooms;

    public GetSessionEndpoint(IRoomService rooms) => _rooms = rooms;

    public override void Configure()
    {
        Get("/api/sessions/{roomName}");
        Policies(VtmPolicies.Staff);
    }

    public override async Task HandleAsync(GetSessionRequest req, CancellationToken ct)
    {
        var room = await _rooms.GetRoomAsync(req.RoomName, ct);
        if (room is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var participants = await _rooms.ListParticipantsAsync(req.RoomName, ct);

        await Send.OkAsync(
            ApiResponse<SessionDetailResponse>.Ok(
                new SessionDetailResponse(
                    SessionMapping.ToSummary(room),
                    participants.Select(SessionMapping.ToSummary).ToList())),
            cancellation: ct);
    }
}
