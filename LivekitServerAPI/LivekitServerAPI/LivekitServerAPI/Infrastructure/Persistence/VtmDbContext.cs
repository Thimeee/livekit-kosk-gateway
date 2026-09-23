using LivekitServerAPI.Domain.Devices;
using LivekitServerAPI.Domain.Operations;
using LivekitServerAPI.Domain.Sessions;
using LivekitServerAPI.Domain.Staff;
using Microsoft.EntityFrameworkCore;

namespace LivekitServerAPI.Infrastructure.Persistence;

/// <summary>
/// The VTM session store. Holds only what exists because VTM exists - sessions, queue state,
/// kiosks, teller availability and the audit trail. Customers, accounts and transactions belong
/// to the bank's core system; see docs/05-data-model.md.
/// </summary>
public class VtmDbContext : DbContext
{
    public VtmDbContext(DbContextOptions<VtmDbContext> options) : base(options) { }

    public DbSet<Kiosk> Kiosks => Set<Kiosk>();
    public DbSet<KioskCredential> KioskCredentials => Set<KioskCredential>();
    public DbSet<Teller> Tellers => Set<Teller>();
    public DbSet<TellerSkill> TellerSkills => Set<TellerSkill>();
    public DbSet<TellerCredential> TellerCredentials => Set<TellerCredential>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<SessionEvent> SessionEvents => Set<SessionEvent>();
    public DbSet<SessionCommand> SessionCommands => Set<SessionCommand>();
    public DbSet<SessionSubmission> SessionSubmissions => Set<SessionSubmission>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<Recording> Recordings => Set<Recording>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Enums are stored as strings, not ints. A DBA reading this schema during an audit
        // should see "Abandoned", not "4", and reordering an enum must not silently rewrite
        // the meaning of existing rows.
        b.ApplyConfigurationsFromAssembly(typeof(VtmDbContext).Assembly);
    }
}
