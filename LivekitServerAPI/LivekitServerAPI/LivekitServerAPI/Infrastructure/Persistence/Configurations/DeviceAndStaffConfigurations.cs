using LivekitServerAPI.Domain.Devices;
using LivekitServerAPI.Domain.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LivekitServerAPI.Infrastructure.Persistence.Configurations;

public class KioskConfiguration : IEntityTypeConfiguration<Kiosk>
{
    public void Configure(EntityTypeBuilder<Kiosk> b)
    {
        b.ToTable("Kiosks");
        b.HasKey(x => x.KioskId);

        b.Property(x => x.KioskId).HasMaxLength(64);
        b.Property(x => x.BranchId).HasMaxLength(64).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(128).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

        // "Which kiosks in this branch are live?" is the dashboard's main query.
        b.HasIndex(x => new { x.BranchId, x.Status });
    }
}

public class KioskCredentialConfiguration : IEntityTypeConfiguration<KioskCredential>
{
    public void Configure(EntityTypeBuilder<KioskCredential> b)
    {
        b.ToTable("KioskCredentials");
        b.HasKey(x => x.Id);

        b.Property(x => x.KioskId).HasMaxLength(64).IsRequired();
        b.Property(x => x.SecretHash).HasMaxLength(256).IsRequired();

        b.HasOne(x => x.Kiosk)
            .WithMany(k => k.Credentials)
            .HasForeignKey(x => x.KioskId)
            // Retiring a kiosk must not erase the credentials that authorised past sessions.
            .OnDelete(DeleteBehavior.Restrict);

        // Authenticating a kiosk looks up its live credentials.
        b.HasIndex(x => new { x.KioskId, x.RevokedAt });
    }
}

public class TellerConfiguration : IEntityTypeConfiguration<Teller>
{
    public void Configure(EntityTypeBuilder<Teller> b)
    {
        b.ToTable("Tellers");
        b.HasKey(x => x.TellerId);

        b.Property(x => x.TellerId).HasMaxLength(128);
        b.Property(x => x.DisplayName).HasMaxLength(128).IsRequired();
        b.Property(x => x.BranchId).HasMaxLength(64).IsRequired();
        b.Property(x => x.Role).HasMaxLength(32).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

        // The ring-out query: who in this branch can take a customer right now.
        b.HasIndex(x => new { x.BranchId, x.Status });
    }
}

public class TellerCredentialConfiguration : IEntityTypeConfiguration<TellerCredential>
{
    public void Configure(EntityTypeBuilder<TellerCredential> b)
    {
        b.ToTable("TellerCredentials");
        b.HasKey(x => x.Id);

        b.Property(x => x.TellerId).HasMaxLength(128).IsRequired();
        b.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();

        b.HasOne(x => x.Teller)
            .WithMany()
            .HasForeignKey(x => x.TellerId)
            // Revoke rather than delete, so a past login can still be explained.
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.TellerId, x.RevokedAt });
    }
}

public class TellerSkillConfiguration : IEntityTypeConfiguration<TellerSkill>
{
    public void Configure(EntityTypeBuilder<TellerSkill> b)
    {
        b.ToTable("TellerSkills");
        b.HasKey(x => new { x.TellerId, x.Skill });

        b.Property(x => x.TellerId).HasMaxLength(128);
        b.Property(x => x.Skill).HasMaxLength(64);

        b.HasOne(x => x.Teller)
            .WithMany(t => t.Skills)
            .HasForeignKey(x => x.TellerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
