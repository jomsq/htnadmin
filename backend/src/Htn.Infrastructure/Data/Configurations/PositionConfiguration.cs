using Htn.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Htn.Infrastructure.Data.Configurations;

internal sealed class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public const string RowVersion = "RowVersion";

    public void Configure(EntityTypeBuilder<Position> b)
    {
        b.ToTable("positions", t =>
        {
            t.HasCheckConstraint("ck_positions_kind", CheckConstraints.In("kind", Enum.GetNames<PositionKind>()));
            t.HasCheckConstraint("ck_positions_holder_since", "(holder_id IS NULL) = (holder_since IS NULL)");
            t.HasCheckConstraint("ck_positions_held_only_if_active", "holder_id IS NULL OR is_active");
        });
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();
        b.Property(p => p.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(p => p.Title).HasMaxLength(Position.TitleMaxLength).IsRequired();
        // Optimistic concurrency on Postgres' xmin system column: two admins assigning/vacating the same position at
        // once means the second save fails (409 concurrent_change) instead of silently overwriting the holder.
        b.Property<uint>(RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();

        b.HasIndex(p => new { p.Kind, p.SortOrder });
        b.HasIndex(p => p.HolderId, "ix_positions_holder_id");
        // A person holds at most one board seat.
        b.HasIndex(p => p.HolderId, "ux_positions_one_board_seat_per_person").IsUnique().HasDatabaseName("ux_positions_one_board_seat_per_person")
            .HasFilter("kind = 'BoardSeat' AND holder_id IS NOT NULL");
        b.HasOne<Person>().WithMany().HasForeignKey(p => p.HolderId).OnDelete(DeleteBehavior.Restrict);

        // The seven board seats are seeded by raw SQL in the Organization_Settings migration (not HasData).
    }
}

internal sealed class PositionAssignmentConfiguration : IEntityTypeConfiguration<PositionAssignment>
{
    public void Configure(EntityTypeBuilder<PositionAssignment> b)
    {
        b.ToTable("position_assignments", t =>
        {
            t.HasCheckConstraint("ck_position_assignments_period", "ended_at IS NULL OR ended_at >= started_at");
            t.HasCheckConstraint("ck_position_assignments_end_reason", "ended_at IS NOT NULL OR (ended_by_id IS NULL AND end_reason IS NULL)");
        });
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedNever();
        b.Property(a => a.EndReason).HasMaxLength(Position.ReasonMaxLength);
        b.HasIndex(a => new { a.PersonId, a.StartedAt });
        // One open term per position.
        b.HasIndex(a => a.PositionId, "ux_position_assignments_current").IsUnique().HasDatabaseName("ux_position_assignments_current").HasFilter("ended_at IS NULL");
        b.HasIndex(a => new { a.PositionId, a.StartedAt });
        b.HasOne<Position>().WithMany().HasForeignKey(a => a.PositionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(a => a.PersonId).OnDelete(DeleteBehavior.Restrict);
    }
}
