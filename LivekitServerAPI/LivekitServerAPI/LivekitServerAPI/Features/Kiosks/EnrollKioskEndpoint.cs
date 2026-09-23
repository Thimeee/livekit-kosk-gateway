using System.Security.Cryptography;
using FastEndpoints;
using LivekitServerAPI.Contracts;
using LivekitServerAPI.Domain.Auth;
using LivekitServerAPI.Domain.Devices;
using LivekitServerAPI.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LivekitServerAPI.Features.Kiosks;

public record EnrollKioskRequest
{
    public string KioskId { get; set; } = string.Empty;

    /// <summary>Revoke every existing credential. Use when a device is replaced or compromised.</summary>
    public bool RevokeExisting { get; set; }
}

public record EnrollKioskResponse(
    string KioskId,
    string Secret,
    DateTimeOffset IssuedAt,
    string Warning);

/// <summary>
/// Issues a device credential for a kiosk.
/// </summary>
/// <remarks>
/// The secret is generated here, returned <b>once</b>, and stored only as a hash. There is no way
/// to retrieve it afterwards - a lost secret means enrolling again, which is the point.
/// <para>
/// Leaving the old credential live by default is deliberate: a kiosk mid-session should not be
/// cut off because someone pre-generated its next secret. Pass <c>revokeExisting</c> when the
/// device is gone or compromised.
/// </para>
/// </remarks>
public class EnrollKioskEndpoint : Endpoint<EnrollKioskRequest, ApiResponse<EnrollKioskResponse>>
{
    private readonly VtmDbContext _db;
    private readonly IPasswordHasher<Kiosk> _hasher;

    public EnrollKioskEndpoint(VtmDbContext db, IPasswordHasher<Kiosk> hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    public override void Configure()
    {
        Post("/api/kiosks/{kioskId}/enroll");
        Policies(VtmPolicies.Admin);
    }

    public override async Task HandleAsync(EnrollKioskRequest req, CancellationToken ct)
    {
        var kiosk = await _db.Kiosks.FirstOrDefaultAsync(k => k.KioskId == req.KioskId, ct);
        if (kiosk is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var now = DateTimeOffset.UtcNow;

        if (req.RevokeExisting)
        {
            var live = await _db.KioskCredentials
                .Where(c => c.KioskId == kiosk.KioskId && c.RevokedAt == null)
                .ToListAsync(ct);

            foreach (var credential in live)
            {
                credential.RevokedAt = now;
            }
        }

        // 32 bytes of CSPRNG output, base64url. Long enough that guessing is not a threat model.
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');

        _db.KioskCredentials.Add(new KioskCredential
        {
            Id = Guid.NewGuid(),
            KioskId = kiosk.KioskId,
            SecretHash = _hasher.HashPassword(kiosk, secret),
            CreatedAt = now,
        });

        kiosk.EnrolledAt ??= now;
        kiosk.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        Logger.LogWarning(
            "Kiosk {KioskId} enrolled by {Actor}, existing credentials revoked: {Revoked}",
            kiosk.KioskId, User.Identity?.Name ?? "unknown", req.RevokeExisting);

        await Send.OkAsync(
            ApiResponse<EnrollKioskResponse>.Ok(
                new EnrollKioskResponse(
                    kiosk.KioskId,
                    secret,
                    now,
                    "Store this secret on the kiosk now. It is not recoverable."),
                "Kiosk enrolled."),
            cancellation: ct);
    }
}
