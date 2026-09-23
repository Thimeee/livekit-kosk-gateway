using FastEndpoints;
using LivekitServerAPI.Contracts;
using LivekitServerAPI.Domain.Auth;
using LivekitServerAPI.Infrastructure.LiveKit;

namespace LivekitServerAPI.Features.Sessions;

/// <summary>
/// Every active session. This is the supervisor dashboard's data source.
/// </summary>
public class ListSessionsEndpoint : EndpointWithoutRequest<ApiResponse<SessionListResponse>>
{
    private readonly IRoomService _rooms;

    public ListSessionsEndpoint(IRoomService rooms) => _rooms = rooms;

    public override void Configure()
    {
        Get("/api/sessions");
        Policies(VtmPolicies.Staff);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var rooms = await _rooms.ListRoomsAsync(ct);
        var sessions = rooms.Select(SessionMapping.ToSummary).ToList();

        await Send.OkAsync(
            ApiResponse<SessionListResponse>.Ok(new SessionListResponse(sessions.Count, sessions)),
            cancellation: ct);
    }
}
