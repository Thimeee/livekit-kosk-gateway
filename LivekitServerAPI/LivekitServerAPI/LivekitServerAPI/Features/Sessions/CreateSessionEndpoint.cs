using FastEndpoints;
using LivekitServerAPI.Contracts;
using LivekitServerAPI.Domain.Auth;
using LivekitServerAPI.Domain.Devices;
using LivekitServerAPI.Domain.Sessions;
using LivekitServerAPI.Infrastructure.LiveKit;
using LivekitServerAPI.Infrastructure.Persistence;
using LivekitServerAPI.Infrastructure.Realtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LivekitServerAPI.Features.Sessions;

/// <summary>
/// Starts a VTM session: creates the room, persists the session, and returns the kiosk token
/// with it so the kiosk needs one round trip.
/// </summary>
/// <remarks>
/// The room name is generated server-side. A caller that could name its own room could ask for
/// a token into somebody else's session.
/// </remarks>
public class CreateSessionEndpoint : Endpoint<CreateSessionRequest, ApiResponse<CreateSessionResponse>>
{
    private readonly IRoomService _rooms;
    private readonly ILiveKitTokenService _tokens;
    private readonly ISessionStore _store;
    private readonly IQueueNotifier _notifier;
    private readonly VtmDbContext _db;
    private readonly LiveKitOptions _options;

    public CreateSessionEndpoint(
        IRoomService rooms, ILiveKitTokenService tokens, ISessionStore store,
        IQueueNotifier notifier, VtmDbContext db, IOptions<LiveKitOptions> options)
    {
        _rooms = rooms;
        _tokens = tokens;
        _store = store;
        _notifier = notifier;
        _db = db;
        _options = options.Value;
    }

    public override void Configure()
    {
        Post("/api/sessions");
        Policies(VtmPolicies.Kiosk);  // a kiosk starts its own session
    }

    public override async Task HandleAsync(CreateSessionRequest req, CancellationToken ct)
    {
        // Only a registered device serves customers. The branch comes from the kiosk record,
        // never from the request - a kiosk must not be able to claim it is somewhere else.
        var kiosk = await _db.Kiosks.FirstOrDefaultAsync(k => k.KioskId == req.KioskId, ct);
        if (kiosk is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (kiosk.Status != KioskStatus.Active)
        {
            // Goes through the same error envelope as a validation failure, so callers
            // parse one shape.
            AddError(r => r.KioskId,
                $"Kiosk '{req.KioskId}' is {kiosk.Status} and cannot start a session.");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        var createdAt = DateTimeOffset.UtcNow;
        var metadata = new SessionMetadata(
            Status: SessionStatus.Waiting.ToString(),
            KioskId: kiosk.KioskId,
            BranchId: kiosk.BranchId,
            CreatedAt: createdAt);

        var room = await _rooms.CreateSessionAsync(metadata, ct);

        // The room exists now; if this throws, we would have an orphaned room. It is cleaned up
        // rather than left behind for the empty-timeout to collect.
        Session session;
        try
        {
            session = await _store.CreateAsync(
                room.Name, room.Sid, kiosk.KioskId, kiosk.BranchId, createdAt, ct);
        }
        catch
        {
            Logger.LogWarning(
                "Persisting session for room {RoomName} failed; deleting the orphaned room",
                room.Name);
            await _rooms.DeleteRoomAsync(room.Name, CancellationToken.None);
            throw;
        }

        kiosk.LastSeenAt = createdAt;
        await _db.SaveChangesAsync(ct);

        // The ring goes out only once the session is durable. A teller pressing Accept on a
        // session that was never persisted would find nothing there.
        await _notifier.CustomerWaitingAsync(session, kiosk.DisplayName, ct);

        var token = _tokens.CreateJoinToken(ParticipantRole.Kiosk, room.Name, kiosk.KioskId);

        Logger.LogInformation(
            "Session {SessionId} started in {RoomName} for kiosk {KioskId} at branch {BranchId}",
            session.SessionId, room.Name, kiosk.KioskId, kiosk.BranchId);

        await Send.ResponseAsync(
            new ApiResponse<CreateSessionResponse>
            {
                Success = true,
                Status = StatusCodes.Status201Created,
                Message = "Session created.",
                Data = new CreateSessionResponse(
                    SessionId: session.SessionId,
                    RoomName: room.Name,
                    RoomSid: room.Sid,
                    BranchId: kiosk.BranchId,
                    Status: metadata.Status,
                    KioskToken: token,
                    KioskIdentity: $"kiosk-{kiosk.KioskId}",
                    TokenExpiresAt: createdAt.Add(_options.TokenTtl),
                    CreatedAt: createdAt),
            },
            StatusCodes.Status201Created,
            cancellation: ct);
    }
}
