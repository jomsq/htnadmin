using Htn.Domain.Audit;
using Htn.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Htn.Infrastructure.Data.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        b.ToTable("audit_entries");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedNever();
        b.Property(a => a.Action).HasMaxLength(100).IsRequired();
        b.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
        b.Property(a => a.EntityId).HasMaxLength(100).IsRequired();
        b.Property(a => a.BeforeJson).HasColumnType("jsonb");
        b.Property(a => a.AfterJson).HasColumnType("jsonb");
        b.Property(a => a.Reason).HasMaxLength(1000);
        b.HasIndex(a => a.At);
        b.HasIndex(a => new { a.EntityType, a.EntityId });
        b.HasIndex(a => a.ActorId);
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages");
        b.HasKey(o => o.Id);
        b.Property(o => o.Id).ValueGeneratedNever();
        b.Property(o => o.Type).HasMaxLength(100).IsRequired();
        b.Property(o => o.PayloadJson).HasColumnType("jsonb").IsRequired();
        b.Property(o => o.Secret).HasMaxLength(500);
        b.Property(o => o.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(o => o.LastError).HasMaxLength(OutboxMessage.LastErrorMaxLength);
        b.Property(o => o.ExternalId).HasMaxLength(200);
        b.HasIndex(o => new { o.Status, o.NextAttemptAt });
    }
}
