using FastEndpoints;
using LivekitServerAPI.Contracts;
using LivekitServerAPI.Domain.Auth;
using LivekitServerAPI.Domain.Sessions;
using LivekitServerAPI.Infrastructure.LiveKit;
using Microsoft.Extensions.Options;

namespace LivekitServerAPI.Features.SessionControl;

/// <summary>
/// Issues a hidden supervisor token so a supervisor can observe a live session.
/// </summary>
/// <remarks>
/// The supervisor joins the <b>same</b> room with <c>Hidden = true</c>, subscribe-only. They see
/// and hear everything; nobody sees them, and they cannot publish.
/// <para>
/// This does not use <c>ForwardParticipant</c>: the self-hosted LiveKit server returns
/// "not implemented" for that RPC (PROJECT.md D-018). Joining hidden is simpler anyway - one room,
/// no mirroring, and <c>MaxSessionParticipants = 3</c> already reserves the seat.
/// </para>
/// </remarks>
public class MonitorSessionEndpoint
    : Endpoint<MonitorSessionRequest, ApiResponse<MonitorSessionResponse>>
{
    private readonly IRoomService _rooms;
    private readonly ILiveKitTokenService _tokens;
    private readonly LiveKitOptions _options;

    public MonitorSessionEndpoint(
        IRoomService rooms, ILiveKitTokenService tokens, IOptions<LiveKitOptions> options)
    {
        _rooms = rooms;
        _tokens = tokens;
        _options = options.Value;
    }

    public override void Configure()
    {
        Post("/api/sessions/{roomName}/monitor");
        Policies(VtmPolicies.Supervisor);  // watching a session unseen is a supervisor act
    }

    public override async Task HandleAsync(MonitorSessionRequest req, CancellationToken ct)
    {
        var room = await _rooms.GetRoomAsync(req.RoomName, ct);
        if (room is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var token = _tokens.CreateJoinToken(
            ParticipantRole.Supervisor, req.RoomName, req.SupervisorId);
        var identity = $"supervisor-{req.SupervisorId}";

        // Warning level on purpose: someone silently watching a customer's session is
        // exactly the kind of event that should stand out in a log review.
        Logger.LogWarning(
            "Supervisor {SupervisorId} is monitoring session {RoomName} - hidden, subscribe-only",
            req.SupervisorId, req.RoomName);

        await Send.OkAsync(
            ApiResponse<MonitorSessionResponse>.Ok(
                new MonitorSessionResponse(
                    req.RoomName, identity, token,
                    DateTimeOffset.UtcNow.Add(_options.TokenTtl))),
            cancellation: ct);
    }
}
