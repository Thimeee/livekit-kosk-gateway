using LivekitServerAPI.Domain.Auth;
using LivekitServerAPI.Domain.Devices;
using LivekitServerAPI.Domain.Staff;
using LivekitServerAPI.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LivekitServerAPI.Infrastructure.Auth;

public sealed class DemoDataOptions
{
    public const string SectionName = "DemoData";

    /// <summary>Off unless explicitly turned on. Never enable this outside a demo.</summary>
    public bool Enabled { get; set; }

    public string Password { get; set; } = string.Empty;
    public string BranchId { get; set; } = "colombo-01";
}

/// <summary>
/// Seeds the two tellers, one supervisor and one kiosk a demo needs, so the flow can be walked
/// end to end without first building user management.
/// </summary>
/// <remarks>
/// This is scaffolding, not a feature. Real teller and kiosk administration is still to come;
/// see docs/PROJECT.md. Nothing here overwrites an existing row.
/// </remarks>
public static class DemoDataSeeder
{
    public static async Task SeedDemoDataAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var options = sp.GetRequiredService<IOptions<DemoDataOptions>>().Value;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DemoDataSeeder));

        if (!options.Enabled || string.IsNullOrWhiteSpace(options.Password))
        {
            return;
        }

        var db = sp.GetRequiredService<VtmDbContext>();
        var hasher = sp.GetRequiredService<IPasswordHasher<Teller>>();
        var now = DateTimeOffset.UtcNow;
        var seeded = new List<string>();

        foreach (var (id, name, role) in new[]
        {
            ("teller1", "Nimal Perera", VtmRoles.Teller),
            ("teller2", "Kamala Silva", VtmRoles.Teller),
            ("super1", "Ranjith Fernando", VtmRoles.Supervisor),
        })
        {
            if (await db.Tellers.AnyAsync(t => t.TellerId == id))
            {
                continue;
            }

            var teller = new Teller
            {
                TellerId = id,
                DisplayName = name,
                BranchId = options.BranchId,
                Role = role,
                Status = TellerStatus.Offline,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.Tellers.Add(teller);
            db.TellerCredentials.Add(new TellerCredential
            {
                Id = Guid.NewGuid(),
                TellerId = id,
                PasswordHash = hasher.HashPassword(teller, options.Password),
                CreatedAt = now,
            });

            seeded.Add($"{id} ({role})");
        }

        if (!await db.Kiosks.AnyAsync(k => k.KioskId == "K-01"))
        {
            db.Kiosks.Add(new Kiosk
            {
                KioskId = "K-01",
                BranchId = options.BranchId,
                DisplayName = "Colombo Main - Lobby 1",
                Status = KioskStatus.Active,
                CreatedAt = now,
                UpdatedAt = now,
            });

            seeded.Add("kiosk K-01");
        }

        if (seeded.Count == 0)
        {
            return;
        }

        await db.SaveChangesAsync();

        logger.LogWarning(
            "Demo data seeded ({Seeded}) at branch {BranchId}. DemoData:Enabled must be false "
            + "anywhere that is not a demo.",
            string.Join(", ", seeded), options.BranchId);
    }
}
