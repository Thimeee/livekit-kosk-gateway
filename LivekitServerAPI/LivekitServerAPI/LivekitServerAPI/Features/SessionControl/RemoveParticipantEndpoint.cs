using FastEndpoints;
using LivekitServerAPI.Contracts;
using LivekitServerAPI.Domain.Auth;
using LivekitServerAPI.Infrastructure.LiveKit;

namespace LivekitServerAPI.Features.SessionControl;

/// <summary>
/// Disconnects a participant from a session.
/// </summary>
/// <remarks>
/// They can rejoin while their token is still valid, which is why token TTL is 15 minutes
/// ([D-015]). To end a session outright, delete the room instead.
/// </remarks>
public class RemoveParticipantEndpoint
    : Endpoint<RemoveParticipantRequest, ApiResponse<RemoveParticipantResponse>>
{
    private readonly IRoomService _rooms;

    public RemoveParticipantEndpoint(IRoomService rooms) => _rooms = rooms;

    public override void Configure()
    {
        Delete("/api/sessions/{roomName}/participants/{identity}");
        Policies(VtmPolicies.Teller);
    }

    public override async Task HandleAsync(RemoveParticipantRequest req, CancellationToken ct)
    {
        var participant = await _rooms.GetParticipantAsync(req.RoomName, req.Identity, ct);
        if (participant is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await _rooms.RemoveParticipantAsync(req.RoomName, req.Identity, ct);

        await Send.OkAsync(
            ApiResponse<RemoveParticipantResponse>.Ok(
                new RemoveParticipantResponse(req.Identity, DateTimeOffset.UtcNow),
                "Participant removed."),
            cancellation: ct);
    }
}
