using LivekitServerAPI.Domain.Devices;
using LivekitServerAPI.Domain.Staff;
using LivekitServerAPI.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LivekitServerAPI.Infrastructure.Auth;

/// <summary>
/// Authenticates tellers against the local <c>TellerCredentials</c> table.
/// </summary>
/// <remarks>
/// Replace with an LDAP bind for on-prem AD, or register nothing at all under
/// <see cref="AuthMode.Oidc"/>. See <see cref="IUserDirectory"/>.
/// </remarks>
public sealed class SqlUserDirectory : IUserDirectory
{
    private readonly VtmDbContext _db;
    private readonly IPasswordHasher<Teller> _hasher;
    private readonly ILogger<SqlUserDirectory> _logger;

    public SqlUserDirectory(
        VtmDbContext db, IPasswordHasher<Teller> hasher, ILogger<SqlUserDirectory> logger)
    {
        _db = db;
        _hasher = hasher;
        _logger = logger;
    }

    public async Task<VtmIdentity?> AuthenticateAsync(
        string username, string password, CancellationToken ct = default)
    {
        var teller = await _db.Tellers.FirstOrDefaultAsync(t => t.TellerId == username, ct);
        var credential = teller is null
            ? null
            : await _db.TellerCredentials
                .Where(c => c.TellerId == username && c.RevokedAt == null)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync(ct);

        if (teller is null || credential is null)
        {
            // Still hash something, so a missing account and a wrong password take the same
            // time. Otherwise the response time tells an attacker which usernames exist.
            _hasher.HashPassword(new Teller(), password);
            _logger.LogWarning("Login failed for {Username}: no active credential", username);
            return null;
        }

        var result = _hasher.VerifyHashedPassword(teller, credential.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("Login failed for {Username}: bad password", username);
            return null;
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            credential.PasswordHash = _hasher.HashPassword(teller, password);
        }

        credential.LastUsedAt = DateTimeOffset.UtcNow;
        teller.LastSeenAt = credential.LastUsedAt;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Login succeeded for {TellerId} ({Role}) at branch {BranchId}",
            teller.TellerId, teller.Role, teller.BranchId);

        return new VtmIdentity(teller.TellerId, teller.DisplayName, teller.Role, teller.BranchId);
    }
}

/// <summary>
/// Authenticates a kiosk against its enrolled device credential.
/// </summary>
/// <remarks>
/// The branch on the returned identity comes from the kiosk record, so a kiosk cannot claim to
/// be somewhere it is not - the same rule the session endpoint relies on (D-020).
/// </remarks>
public sealed class SqlKioskDirectory : IKioskDirectory
{
    private readonly VtmDbContext _db;
    private readonly IPasswordHasher<Kiosk> _hasher;
    private readonly ILogger<SqlKioskDirectory> _logger;

    public SqlKioskDirectory(
        VtmDbContext db, IPasswordHasher<Kiosk> hasher, ILogger<SqlKioskDirectory> logger)
    {
        _db = db;
        _hasher = hasher;
        _logger = logger;
    }

    public async Task<VtmIdentity?> AuthenticateAsync(
        string kioskId, string secret, CancellationToken ct = default)
    {
        var kiosk = await _db.Kiosks.FirstOrDefaultAsync(k => k.KioskId == kioskId, ct);

        if (kiosk is null || kiosk.Status != KioskStatus.Active)
        {
            _hasher.HashPassword(new Kiosk(), secret);
            _logger.LogWarning(
                "Kiosk auth failed for {KioskId}: unknown or not active", kioskId);
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var credentials = await _db.KioskCredentials
            .Where(c => c.KioskId == kioskId && c.RevokedAt == null)
            .ToListAsync(ct);

        // A kiosk may hold more than one live credential during a rotation, so every
        // unexpired one is a candidate.
        var match = credentials.FirstOrDefault(c =>
            (c.ExpiresAt is null || c.ExpiresAt > now)
            && _hasher.VerifyHashedPassword(kiosk, c.SecretHash, secret)
               != PasswordVerificationResult.Failed);

        if (match is null)
        {
            _logger.LogWarning("Kiosk auth failed for {KioskId}: no matching credential", kioskId);
            return null;
        }

        kiosk.LastSeenAt = now;
        await _db.SaveChangesAsync(ct);

        return new VtmIdentity(
            kiosk.KioskId, kiosk.DisplayName, Domain.Auth.VtmRoles.Kiosk, kiosk.BranchId);
    }
}
