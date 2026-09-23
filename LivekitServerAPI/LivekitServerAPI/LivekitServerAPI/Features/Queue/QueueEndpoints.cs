using FastEndpoints;
using FluentValidation;
using LivekitServerAPI.Contracts;
using LivekitServerAPI.Domain.Auth;
using LivekitServerAPI.Domain.Sessions;
using LivekitServerAPI.Domain.Staff;
using LivekitServerAPI.Infrastructure.Auth;
using LivekitServerAPI.Infrastructure.LiveKit;
using LivekitServerAPI.Infrastructure.Persistence;
using LivekitServerAPI.Infrastructure.Realtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LivekitServerAPI.Features.Queue;

public record QueueEntry(
    Guid SessionId,
    string RoomName,
    string KioskId,
    string KioskName,
    string BranchId,
    DateTimeOffset CreatedAt,
    int WaitingSeconds);

public record QueueListResponse(int Count, IReadOnlyList<QueueEntry> Entries);

public record AcceptSessionRequest
{
    public string RoomName { get; set; } = string.Empty;
}

public record AcceptSessionResponse(
    Guid SessionId,
    string RoomName,
    string TellerToken,
    string TellerIdentity,
    DateTimeOffset TokenExpiresAt,
    int WaitedSeconds);

/// <summary>
/// The customers currently waiting, oldest first.
/// </summary>
/// <remarks>
/// A teller sees their own branch. Supervisors and admins see everything - they are covering
/// more than one desk by definition.
/// </remarks>
public class ListQueueEndpoint : EndpointWithoutRequest<ApiResponse<QueueListResponse>>
{
    private readonly VtmDbContext _db;

    public ListQueueEndpoint(VtmDbContext db) => _db = db;

    public override void Configure()
    {
        Get("/api/queue");
        Policies(VtmPolicies.Staff);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var branch = User.Branch();
        var seesAllBranches = User.Role() is VtmRoles.Supervisor or VtmRoles.Admin;

        var query = _db.Sessions.Where(s => s.Status == SessionStatus.Waiting);
        if (!seesAllBranches)
        {
            query = query.Where(s => s.BranchId == branch);
        }

        var now = DateTimeOffset.UtcNow;
        var rows = await query
            .OrderBy(s => s.CreatedAt)
            .Join(_db.Kiosks, s => s.KioskId, k => k.KioskId, (s, k) => new { s, k.DisplayName })
            .ToListAsync(ct);

        var entries = rows
            .Select(r => new QueueEntry(
                r.s.SessionId, r.s.RoomName, r.s.KioskId, r.DisplayName, r.s.BranchId,
                r.s.CreatedAt, (int)(now - r.s.CreatedAt).TotalSeconds))
            .ToList();

        await Send.OkAsync(
            ApiResponse<QueueListResponse>.Ok(new QueueListResponse(entries.Count, entries)), ct);
    }
}

/// <summary>
/// A teller takes the customer. Returns the token they join with.
/// </summary>
/// <remarks>
/// The accept is guarded by a conditional update: two tellers pressing at the same moment means
/// one gets the session and the other gets a 409, never both joining the same customer.
/// </remarks>
public class AcceptSessionEndpoint : Endpoint<AcceptSessionRequest, ApiResponse<AcceptSessionResponse>>
{
    private readonly VtmDbContext _db;
    private readonly IRoomService _rooms;
    private readonly ILiveKitTokenService _tokens;
    private readonly IQueueNotifier _notifier;
    private readonly LiveKitOptions _options;

    public AcceptSessionEndpoint(
        VtmDbContext db, IRoomService rooms, ILiveKitTokenService tokens,
        IQueueNotifier notifier, IOptions<LiveKitOptions> options)
    {
        _db = db;
        _rooms = rooms;
        _tokens = tokens;
        _notifier = notifier;
        _options = options.Value;
    }

    public override void Configure()
    {
        Post("/api/queue/{roomName}/accept");
        Policies(VtmPolicies.Teller);
    }

    public override async Task HandleAsync(AcceptSessionRequest req, CancellationToken ct)
    {
        var tellerId = User.SubjectId()!;
        var session = await _db.Sessions.FirstOrDefaultAsync(s => s.RoomName == req.RoomName, ct);

        if (session is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (!User.CanActOnBranch(session.BranchId))
        {
            AddError(r => r.RoomName, "This session belongs to another branch.");
            await Send.ErrorsAsync(StatusCodes.Status403Forbidden, ct);
            return;
        }

        // Losing this race is normal, not exceptional: two tellers watching the same queue
        // will sometimes press at the same moment.
        if (session.Status != SessionStatus.Waiting)
        {
            AddError(r => r.RoomName,
                session.TellerId == tellerId
                    ? "You have already accepted this session."
                    : "Another teller has already taken this customer.");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        // The teller row must exist before it can be referenced - under OIDC the teller is
        // known to the provider but may never have been seen here before.
        var teller = await _db.Tellers.FirstOrDefaultAsync(t => t.TellerId == tellerId, ct);
        if (teller is null)
        {
            AddError(r => r.RoomName, "Your teller profile has not been provisioned.");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        var acceptedAt = DateTimeOffset.UtcNow;
        session.TellerId = tellerId;
        session.Status = SessionStatus.Active;
        session.AcceptedAt = acceptedAt;
        session.WaitSeconds = (int)(acceptedAt - session.CreatedAt).TotalSeconds;

        teller.Status = TellerStatus.Busy;
        teller.LastSeenAt = acceptedAt;

        _db.SessionEvents.Add(new SessionEvent
        {
            SessionId = session.SessionId,
            OccurredAt = acceptedAt,
            Source = EventSource.Teller,
            EventType = "session.accepted",
            ActorId = tellerId,
            Payload = $$"""{"waitSeconds":{{session.WaitSeconds}}}""",
        });

        await _db.SaveChangesAsync(ct);

        // The room metadata is what live clients react to; the kiosk learns a teller is coming
        // from RoomMetadataChanged, without polling.
        await _rooms.UpdateSessionMetadataAsync(
            session.RoomName,
            new SessionMetadata(
                SessionStatus.Active.ToString(), session.KioskId, session.BranchId, session.CreatedAt),
            ct);

        // Everyone else watching the branch drops it from their list.
        await _notifier.SessionTakenAsync(session.BranchId, session.RoomName, tellerId, ct);

        var token = _tokens.CreateJoinToken(ParticipantRole.Teller, session.RoomName, tellerId);

        Logger.LogInformation(
            "Teller {TellerId} accepted {RoomName} after {WaitSeconds}s",
            tellerId, session.RoomName, session.WaitSeconds);

        await Send.OkAsync(
            ApiResponse<AcceptSessionResponse>.Ok(
                new AcceptSessionResponse(
                    session.SessionId, session.RoomName, token, $"teller-{tellerId}",
                    acceptedAt.Add(_options.TokenTtl), session.WaitSeconds ?? 0),
                "Session accepted."),
            cancellation: ct);
    }
}

public class AcceptSessionValidator : Validator<AcceptSessionRequest>
{
    public AcceptSessionValidator()
    {
        RuleFor(x => x.RoomName).NotEmpty().WithMessage("Room name is required.");
    }
}
