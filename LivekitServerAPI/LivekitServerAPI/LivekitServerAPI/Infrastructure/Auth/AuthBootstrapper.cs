using LivekitServerAPI.Domain.Auth;
using LivekitServerAPI.Domain.Staff;
using LivekitServerAPI.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LivekitServerAPI.Infrastructure.Auth;

/// <summary>
/// Seeds the first administrator, so there is someone who can register kiosks and create tellers
/// on a fresh database.
/// </summary>
/// <remarks>
/// Runs only under <see cref="AuthMode.Local"/>, only when a bootstrap password is configured,
/// and only when the account does not already exist. It never overwrites an existing credential -
/// once someone has changed the password, re-running must not put the seeded one back.
/// </remarks>
public static class AuthBootstrapper
{
    public static async Task SeedBootstrapAdminAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var options = sp.GetRequiredService<IOptions<AuthOptions>>().Value;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(AuthBootstrapper));

        if (options.Mode != AuthMode.Local || string.IsNullOrWhiteSpace(options.BootstrapAdmin.Password))
        {
            return;
        }

        var db = sp.GetRequiredService<VtmDbContext>();
        var admin = options.BootstrapAdmin;

        if (await db.Tellers.AnyAsync(t => t.TellerId == admin.TellerId))
        {
            logger.LogDebug("Bootstrap admin {TellerId} already exists; leaving it alone",
                admin.TellerId);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var teller = new Teller
        {
            TellerId = admin.TellerId,
            DisplayName = admin.DisplayName,
            BranchId = admin.BranchId,
            Role = VtmRoles.Admin,
            Status = TellerStatus.Offline,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Tellers.Add(teller);
        db.TellerCredentials.Add(new TellerCredential
        {
            Id = Guid.NewGuid(),
            TellerId = teller.TellerId,
            PasswordHash = sp.GetRequiredService<IPasswordHasher<Teller>>()
                .HashPassword(teller, admin.Password),
            CreatedAt = now,
        });

        await db.SaveChangesAsync();

        logger.LogWarning(
            "Seeded bootstrap administrator {TellerId}. Change this password and clear "
            + "Auth:BootstrapAdmin:Password before this reaches anything but a dev machine.",
            teller.TellerId);
    }
}
