using FastEndpoints;
using FluentValidation;
using LivekitServerAPI.Contracts;
using LivekitServerAPI.Domain.Auth;
using LivekitServerAPI.Domain.Staff;
using LivekitServerAPI.Infrastructure.Auth;
using LivekitServerAPI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LivekitServerAPI.Features.Me;

public record MeResponse(
    string TellerId,
    string DisplayName,
    string Role,
    string BranchId,
    string Status);

public record UpdateMyStatusRequest(string Status);

public class UpdateMyStatusValidator : Validator<UpdateMyStatusRequest>
{
    public UpdateMyStatusValidator()
    {
        RuleFor(x => x.Status)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Status is required.")
            .Must(s => Enum.TryParse<TellerStatus>(s, ignoreCase: true, out _))
            .WithMessage("Status must be one of: offline, available, busy, away.");
    }
}

/// <summary>Who the caller is, as this API sees them.</summary>
public class GetMeEndpoint : EndpointWithoutRequest<ApiResponse<MeResponse>>
{
    private readonly VtmDbContext _db;

    public GetMeEndpoint(VtmDbContext db) => _db = db;

    public override void Configure()
    {
        Get("/api/me");
        Policies(VtmPolicies.Staff);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = User.SubjectId()!;
        var teller = await _db.Tellers.FirstOrDefaultAsync(t => t.TellerId == id, ct);

        // The claims are authoritative for identity; the row only adds VTM state. Under OIDC
        // the row may not exist yet, and that is not an error.
        await Send.OkAsync(
            ApiResponse<MeResponse>.Ok(
                new MeResponse(
                    id,
                    teller?.DisplayName ?? id,
                    User.Role() ?? VtmRoles.Teller,
                    User.Branch() ?? teller?.BranchId ?? string.Empty,
                    (teller?.Status ?? TellerStatus.Offline).ToString())),
            cancellation: ct);
    }
}

/// <summary>
/// Sets the caller's availability. This is what decides who the ring reaches.
/// </summary>
public class UpdateMyStatusEndpoint : Endpoint<UpdateMyStatusRequest, ApiResponse<MeResponse>>
{
    private readonly VtmDbContext _db;

    public UpdateMyStatusEndpoint(VtmDbContext db) => _db = db;

    public override void Configure()
    {
        Patch("/api/me/status");
        Policies(VtmPolicies.Staff);
    }

    public override async Task HandleAsync(UpdateMyStatusRequest req, CancellationToken ct)
    {
        var id = User.SubjectId()!;
        var teller = await _db.Tellers.FirstOrDefaultAsync(t => t.TellerId == id, ct);

        if (teller is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var status = Enum.Parse<TellerStatus>(req.Status, ignoreCase: true);
        teller.Status = status;
        teller.LastSeenAt = DateTimeOffset.UtcNow;
        teller.UpdatedAt = teller.LastSeenAt.Value;
        await _db.SaveChangesAsync(ct);

        Logger.LogInformation("Teller {TellerId} is now {Status}", id, status);

        await Send.OkAsync(
            ApiResponse<MeResponse>.Ok(
                new MeResponse(
                    teller.TellerId, teller.DisplayName, teller.Role,
                    teller.BranchId, teller.Status.ToString())),
            cancellation: ct);
    }
}
