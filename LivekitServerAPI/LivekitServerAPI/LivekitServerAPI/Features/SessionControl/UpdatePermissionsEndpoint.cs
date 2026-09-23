using FastEndpoints;
using LivekitServerAPI.Contracts;
using LivekitServerAPI.Domain.Auth;
using LivekitServerAPI.Infrastructure.LiveKit;

namespace LivekitServerAPI.Features.SessionControl;

/// <summary>
/// Changes what a participant may do without reissuing their token.
/// </summary>
/// <remarks>
/// Revoking <c>canPublish</c> is how a customer is put into a read-only waiting state
/// mid-session. Subscription is left untouched - they keep seeing and hearing the teller.
/// </remarks>
public class UpdatePermissionsEndpoint
    : Endpoint<UpdatePermissionsRequest, ApiResponse<UpdatePermissionsResponse>>
{
    private readonly IRoomService _rooms;

    public UpdatePermissionsEndpoint(IRoomService rooms) => _rooms = rooms;

    public override void Configure()
    {
        Patch("/api/sessions/{roomName}/participants/{identity}");
        Policies(VtmPolicies.Teller);
    }

    public override async Task HandleAsync(UpdatePermissionsRequest req, CancellationToken ct)
    {
        var participant = await _rooms.GetParticipantAsync(req.RoomName, req.Identity, ct);
        if (participant is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await _rooms.UpdateParticipantPermissionsAsync(
            req.RoomName, req.Identity, req.CanPublish, req.CanPublishData, ct);

        await Send.OkAsync(
            ApiResponse<UpdatePermissionsResponse>.Ok(
                new UpdatePermissionsResponse(req.Identity, req.CanPublish, req.CanPublishData)),
            cancellation: ct);
    }
}
