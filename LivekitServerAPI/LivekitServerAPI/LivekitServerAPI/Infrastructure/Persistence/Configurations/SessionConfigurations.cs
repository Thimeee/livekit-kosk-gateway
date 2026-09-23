using LivekitServerAPI.Domain.Operations;
using LivekitServerAPI.Domain.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LivekitServerAPI.Infrastructure.Persistence.Configurations;

public class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> b)
    {
        b.ToTable("Sessions");
        b.HasKey(x => x.SessionId);

        b.Property(x => x.RoomName).HasMaxLength(128).IsRequired();
        b.Property(x => x.RoomSid).HasMaxLength(64);
        b.Property(x => x.KioskId).HasMaxLength(64).IsRequired();
        b.Property(x => x.BranchId).HasMaxLength(64).IsRequired();
        b.Property(x => x.TellerId).HasMaxLength(128);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(x => x.EndReason).HasConversion<string>().HasMaxLength(32);

        // The room name is how a webhook, and the core banking record, find their way back
        // to this row. It has to be unique.
        b.HasIndex(x => x.RoomName).IsUnique();

        // The queue query: what is waiting in this branch, oldest first.
        b.HasIndex(x => new { x.Status, x.BranchId, x.CreatedAt });

        // Reporting over a date range.
        b.HasIndex(x => x.CreatedAt);

        b.HasOne(x => x.Kiosk)
            .WithMany()
            .HasForeignKey(x => x.KioskId)
            // A kiosk that has ever held a session cannot be deleted out from under its history.
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Teller)
            .WithMany()
            .HasForeignKey(x => x.TellerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class SessionEventConfiguration : IEntityTypeConfiguration<SessionEvent>
{
    public void Configure(EntityTypeBuilder<SessionEvent> b)
    {
        b.ToTable("SessionEvents");
        b.HasKey(x => x.Id);

        b.Property(x => x.Source).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.EventType).HasMaxLength(64).IsRequired();
        b.Property(x => x.ActorId).HasMaxLength(128);
        b.Property(x => x.Payload).HasMaxLength(4000);

        // Reading one session's timeline in order is the only query this table serves.
        b.HasIndex(x => new { x.SessionId, x.OccurredAt });

        b.HasOne(x => x.Session)
            .WithMany(s => s.Events)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SessionCommandConfiguration : IEntityTypeConfiguration<SessionCommand>
{
    public void Configure(EntityTypeBuilder<SessionCommand> b)
    {
        b.ToTable("SessionCommands");
        b.HasKey(x => x.Id);

        b.Property(x => x.Command).HasMaxLength(64).IsRequired();
        b.Property(x => x.RequestedBy).HasMaxLength(128).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.ResultPayload).HasMaxLength(4000);
        b.Property(x => x.ErrorMessage).HasMaxLength(1000);

        b.HasIndex(x => new { x.SessionId, x.RequestedAt });

        b.HasOne(x => x.Session)
            .WithMany(s => s.Commands)
            .HasForeignKey(x => x.SessionId)
            // Deliberately NOT cascade: a card read or a print happened in the real world.
            // Deleting a session must not quietly erase the record of it.
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class SessionSubmissionConfiguration : IEntityTypeConfiguration<SessionSubmission>
{
    public void Configure(EntityTypeBuilder<SessionSubmission> b)
    {
        b.ToTable("SessionSubmissions");
        b.HasKey(x => x.Id);

        b.Property(x => x.TellerId).HasMaxLength(128).IsRequired();
        b.Property(x => x.OperationType).HasMaxLength(64).IsRequired();
        b.Property(x => x.CoreReference).HasMaxLength(128).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

        // "Which session produced core reference X?" - the reconciliation query.
        b.HasIndex(x => x.CoreReference);
        b.HasIndex(x => new { x.SessionId, x.SubmittedAt });

        b.HasOne(x => x.Session)
            .WithMany(s => s.Submissions)
            .HasForeignKey(x => x.SessionId)
            // Same reasoning as commands, and stronger: this points at a real transaction.
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> b)
    {
        b.ToTable("WebhookDeliveries");
        b.HasKey(x => x.EventId);

        b.Property(x => x.EventId).HasMaxLength(128);
        b.Property(x => x.EventType).HasMaxLength(64).IsRequired();

        // Lets old rows be pruned without a table scan.
        b.HasIndex(x => x.ReceivedAt);
    }
}

public class RecordingConfiguration : IEntityTypeConfiguration<Recording>
{
    public void Configure(EntityTypeBuilder<Recording> b)
    {
        b.ToTable("Recordings");
        b.HasKey(x => x.Id);

        b.Property(x => x.EgressId).HasMaxLength(128).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.FileUri).HasMaxLength(1024);

        b.HasIndex(x => x.EgressId);
        b.HasIndex(x => x.SessionId);

        b.HasOne(x => x.Session)
            .WithMany()
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
