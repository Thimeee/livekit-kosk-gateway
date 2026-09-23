using FastEndpoints;
using LivekitServerAPI.Contracts;
using LivekitServerAPI.Domain.Auth;
using LivekitServerAPI.Domain.Sessions;
using LivekitServerAPI.Infrastructure.LiveKit;
using Microsoft.Extensions.Options;

namespace LivekitServerAPI.Features.SessionControl;

/// <summary>
/// Hands a session to a different teller. The customer does not move.
/// </summary>
/// <remarks>
/// The current teller is removed from the room and a token is issued to the incoming one for the
/// <b>same</b> room. From the customer's side one person leaves and another arrives; their own
/// connection, tracks and screen share are untouched.
/// <para>
/// Deliberately not <c>MoveParticipant</c>. That RPC returns "not implemented" on the self-hosted
/// server (PROJECT.md D-018), and moving the <i>customer</i> to a new room would be the wrong shape
/// regardless - it would tear down and rebuild their media for a change that has nothing to do
/// with them.
/// </para>
/// </remarks>
public class TransferSessionEndpoint
    : Endpoint<TransferSessionRequest, ApiResponse<TransferSessionResponse>>
{
    private const string TellerIdentityPrefix = "teller-";

    private readonly IRoomService _rooms;
    private readonly ILiveKitTokenService _tokens;
    private readonly LiveKitOptions _options;

    public TransferSessionEndpoint(
        IRoomService rooms, ILiveKitTokenService tokens, IOptions<LiveKitOptions> options)
    {
        _rooms = rooms;
        _tokens = tokens;
        _options = options.Value;
    }

    public override void Configure()
    {
        Post("/api/sessions/{roomName}/transfer");
        Policies(VtmPolicies.Teller);
    }

    public override async Task HandleAsync(TransferSessionRequest req, CancellationToken ct)
    {
        var room = await _rooms.GetRoomAsync(req.RoomName, ct);
        if (room is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var participants = await _rooms.ListParticipantsAsync(req.RoomName, ct);
        var outgoing = participants.FirstOrDefault(
            p => p.Identity.StartsWith(TellerIdentityPrefix, StringComparison.Ordinal));

        // An unattended session is a valid case, not an error: the first teller may have
        // dropped out already, and a transfer is how the next one picks the customer up.
        if (outgoing is not null)
        {
            await _rooms.RemoveParticipantAsync(req.RoomName, outgoing.Identity, ct);
        }

        var token = _tokens.CreateJoinToken(ParticipantRole.Teller, req.RoomName, req.ToTellerId);
        var identity = TellerIdentityPrefix + req.ToTellerId;

        Logger.LogInformation(
            "Session {RoomName} transferred from {FromTeller} to {ToTeller}",
            req.RoomName, outgoing?.Identity ?? "(unattended)", identity);

        await Send.OkAsync(
            ApiResponse<TransferSessionResponse>.Ok(
                new TransferSessionResponse(
                    req.RoomName,
                    outgoing?.Identity,
                    identity,
                    token,
                    DateTimeOffset.UtcNow.Add(_options.TokenTtl)),
                "Session transferred."),
            cancellation: ct);
    }
}
