using FastEndpoints;
using FluentValidation;
using LivekitServerAPI.Contracts;
using LivekitServerAPI.Domain.Auth;
using LivekitServerAPI.Domain.Devices;
using LivekitServerAPI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LivekitServerAPI.Features.Kiosks;

public record RegisterKioskRequest(string KioskId, string BranchId, string DisplayName);

public record KioskResponse(
    string KioskId,
    string BranchId,
    string DisplayName,
    string Status,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset CreatedAt);

public record KioskListResponse(int Count, IReadOnlyList<KioskResponse> Kiosks);

public class RegisterKioskValidator : Validator<RegisterKioskRequest>
{
    public RegisterKioskValidator()
    {
        RuleFor(x => x.KioskId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Kiosk id is required.")
            .MaximumLength(64).WithMessage("Kiosk id must be 64 characters or fewer.");

        RuleFor(x => x.BranchId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Branch id is required.")
            .MaximumLength(64).WithMessage("Branch id must be 64 characters or fewer.");

        RuleFor(x => x.DisplayName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Display name is required.")
            .MaximumLength(128).WithMessage("Display name must be 128 characters or fewer.");
    }
}

/// <summary>
/// Registers a kiosk. Until a kiosk exists here it cannot start a session - the FK from
/// <c>Sessions</c> enforces that, which is the point: only known devices serve customers.
/// </summary>
/// <remarks>
/// Registration is not enrollment. This creates the device in <c>Provisioned</c> state;
/// issuing it a credential comes with the auth work (K-7). For now it is set Active so
/// sessions can be created.
/// </remarks>
public class RegisterKioskEndpoint : Endpoint<RegisterKioskRequest, ApiResponse<KioskResponse>>
{
    private readonly VtmDbContext _db;

    public RegisterKioskEndpoint(VtmDbContext db) => _db = db;

    public override void Configure()
    {
        Post("/api/kiosks");
        Policies(VtmPolicies.Admin);
    }

    public override async Task HandleAsync(RegisterKioskRequest req, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await _db.Kiosks.FirstOrDefaultAsync(k => k.KioskId == req.KioskId, ct);

        if (existing is not null)
        {
            // Re-registering updates the details rather than failing: a kiosk being moved to
            // another branch or renamed is routine, and its session history must survive it.
            existing.BranchId = req.BranchId;
            existing.DisplayName = req.DisplayName;
            existing.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);

            Logger.LogInformation("Kiosk {KioskId} re-registered to branch {BranchId}",
                req.KioskId, req.BranchId);

            await Send.OkAsync(ApiResponse<KioskResponse>.Ok(Map(existing), "Kiosk updated."), ct);
            return;
        }

        var kiosk = new Kiosk
        {
            KioskId = req.KioskId,
            BranchId = req.BranchId,
            DisplayName = req.DisplayName,
            Status = KioskStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.Kiosks.Add(kiosk);
        await _db.SaveChangesAsync(ct);

        Logger.LogInformation("Kiosk {KioskId} registered at branch {BranchId}",
            req.KioskId, req.BranchId);

        await Send.ResponseAsync(
            new ApiResponse<KioskResponse>
            {
                Success = true,
                Status = StatusCodes.Status201Created,
                Message = "Kiosk registered.",
                Data = Map(kiosk),
            },
            StatusCodes.Status201Created,
            cancellation: ct);
    }

    internal static KioskResponse Map(Kiosk k) =>
        new(k.KioskId, k.BranchId, k.DisplayName, k.Status.ToString(), k.LastSeenAt, k.CreatedAt);
}

public class ListKiosksEndpoint : EndpointWithoutRequest<ApiResponse<KioskListResponse>>
{
    private readonly VtmDbContext _db;

    public ListKiosksEndpoint(VtmDbContext db) => _db = db;

    public override void Configure()
    {
        Get("/api/kiosks");
        Policies(VtmPolicies.Admin);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var kiosks = await _db.Kiosks
            .OrderBy(k => k.BranchId).ThenBy(k => k.KioskId)
            .Select(k => new KioskResponse(
                k.KioskId, k.BranchId, k.DisplayName, k.Status.ToString(), k.LastSeenAt, k.CreatedAt))
            .ToListAsync(ct);

        await Send.OkAsync(
            ApiResponse<KioskListResponse>.Ok(new KioskListResponse(kiosks.Count, kiosks)), ct);
    }
}
