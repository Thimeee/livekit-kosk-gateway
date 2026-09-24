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
/// The open session the caller belongs to, if there is one.
/// </summary>
/// <remarks>
/// A page reload or an application restart loses everything held in memory, including which room
/// the caller was in. Without this a teller who refreshed had no way back - the session had left
/// the queue when they accepted it - and a kiosk that restarted created a brand new session while
/// its customer's call was still open. Both now ask here first and rejoin what they find.
///
/// A session only counts while its LiveKit room still exists. Once the room is gone the call is
/// over whatever the database says, and handing out that room name would let the caller recreate
/// an empty room of the same name - LiveKit creates rooms on join - and sit in it alone.
/// </remarks>
public class CurrentSessionEndpoint : EndpointWithoutRequest<ApiResponse<CurrentSessionResponse>>
{
    private readonly VtmDbContext _db;
    private readonly IRoomService _rooms;

    public CurrentSessionEndpoint(VtmDbContext db, IRoomService rooms)
    {
        _db = db;
        _rooms = rooms;
    }

    public override void Configure()
    {
        // A literal segment, so it is matched before /api/sessions/{roomName}.
        Get("/api/sessions/current");
        Policies(VtmPolicies.SessionParticipant);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var subject = User.SubjectId();
        var role = User.Role();

        if (subject is null || role is null)
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        // A kiosk owns a session from the moment it is created; staff only once they accept it.
        var query = role == VtmRoles.Kiosk
            ? _db.Sessions.Where(s => s.KioskId == subject
                                      && (s.Status == SessionStatus.Waiting || s.Status == SessionStatus.Active))
            : _db.Sessions.Where(s => s.TellerId == subject && s.Status == SessionStatus.Active);

        var session = await query.OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync(ct);

        if (session is null || await _rooms.GetRoomAsync(session.RoomName, ct) is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(
            ApiResponse<CurrentSessionResponse>.Ok(
                new CurrentSessionResponse(session.RoomName, session.Status.ToString())),
            cancellation: ct);
    }
}
