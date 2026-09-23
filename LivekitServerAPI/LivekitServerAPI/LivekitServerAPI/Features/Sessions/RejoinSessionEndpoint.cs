using FastEndpoints;
using LivekitServerAPI.Contracts;
using LivekitServerAPI.Domain.Auth;
using LivekitServerAPI.Domain.Sessions;
using LivekitServerAPI.Infrastructure.Auth;
using LivekitServerAPI.Infrastructure.LiveKit;
using LivekitServerAPI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LivekitServerAPI.Features.Sessions;

/// <summary>
/// Issues a fresh LiveKit token for a session the caller is already part of.
/// </summary>
/// <remarks>
/// Not every disconnect heals itself. A signal reconnect does - the SDK restores the session and
/// the data channel keeps running. A <c>server-leave</c> does not: the client ends up
/// <c>Disconnected</c> and stays there, measured on this project (PROJECT.md D-029).
///
/// Without this, the only way back into a room was to start a *new* session, which would ring the
/// queue a second time and abandon the original. So a client that is thrown out asks for another
/// token for the room it was already in, and rejoins it.
///
/// It deliberately does not create anything. If the session has ended, the answer is 404 and the
/// client goes back to idle - which is the one case where returning to idle is right.
/// </remarks>
public class RejoinSessionEndpoint
    : Endpoint<RejoinSessionRequest, ApiResponse<RejoinSessionResponse>>
{
    private readonly VtmDbContext _db;
    private readonly ILiveKitTokenService _tokens;

    public RejoinSessionEndpoint(VtmDbContext db, ILiveKitTokenService tokens)
    {
        _db = db;
        _tokens = tokens;
    }

    public override void Configure()
    {
        Post("/api/sessions/{roomName}/rejoin");

        // Both sides of a call can be dropped, so both can ask to come back. Which token they
        // get is decided from their own claims, never from the request.
        //
        // One policy, not two: listing two requires BOTH, which refused every kiosk with a 403.
        Policies(VtmPolicies.SessionParticipant);
    }

    public override async Task HandleAsync(RejoinSessionRequest req, CancellationToken ct)
    {
        var session = await _db.Sessions
            .FirstOrDefaultAsync(s => s.RoomName == req.RoomName, ct);

        // An allow-list, not a check for Ended. A session ended before any teller accepted is
        // Abandoned rather than Ended, and checking only for Ended let a kiosk rejoin one -
        // it then sat on "Reconnecting..." forever instead of returning to its idle screen.
        // Listing what IS rejoinable also means a terminal status added later cannot quietly
        // become rejoinable by default.
        var rejoinable = session?.Status is SessionStatus.Waiting or SessionStatus.Active;

        if (session is null || !rejoinable)
        {
            // Nothing to rejoin. The caller should stop trying and go back to idle.
            await Send.NotFoundAsync(ct);
            return;
        }

        var role = User.Role();
        var subject = User.SubjectId();

        if (subject is null || role is null)
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        // A caller may only rejoin the session they were already in. Without this check any
        // kiosk could take any other kiosk's customer, and any teller could walk into a
        // colleague's call.
        var allowed = role switch
        {
            VtmRoles.Kiosk => session.KioskId == subject,
            VtmRoles.Teller => session.TellerId == subject,
            VtmRoles.Supervisor or VtmRoles.Admin => User.CanActOnBranch(session.BranchId),
            _ => false,
        };

        if (!allowed)
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        var participantRole = role == VtmRoles.Kiosk ? ParticipantRole.Kiosk : ParticipantRole.Teller;
        var token = _tokens.CreateJoinToken(participantRole, session.RoomName, subject);

        Logger.LogInformation(
            "{Role} {Subject} rejoined {RoomName}", role, subject, session.RoomName);

        await Send.OkAsync(
            ApiResponse<RejoinSessionResponse>.Ok(
                new RejoinSessionResponse(session.RoomName, token, session.Status.ToString())),
            cancellation: ct);
    }
}
