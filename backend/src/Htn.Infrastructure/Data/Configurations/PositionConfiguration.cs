using Htn.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Htn.Infrastructure.Data.Configurations;

internal sealed class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    /// <summary>The seven Board of Trustees seats, seeded in every environment by the Organization_Settings migration.</summary>
    public static readonly (Guid Id, string Title, int Sort)[] BoardSeats =
    [
        (new Guid("0192f0a0-0000-7000-8000-0000000b0001"), "Chairperson", 1),
        (new Guid("0192f0a0-0000-7000-8000-0000000b0002"), "Vice Chairperson", 2),
        (new Guid("0192f0a0-0000-7000-8000-0000000b0003"), "Treasurer", 3),
        (new Guid("0192f0a0-0000-7000-8000-0000000b0004"), "Corporate Secretary", 4),
        (new Guid("0192f0a0-0000-7000-8000-0000000b0005"), "Trustee", 5),
        (new Guid("0192f0a0-0000-7000-8000-0000000b0006"), "Trustee", 6),
        (new Guid("0192f0a0-0000-7000-8000-0000000b0007"), "Trustee", 7),
    ];

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

        b.HasIndex(p => new { p.Kind, p.SortOrder });
        b.HasIndex(p => p.HolderId, "ix_positions_holder_id");
        // A person holds at most one board seat.
        b.HasIndex(p => p.HolderId, "ux_positions_one_board_seat_per_person").IsUnique().HasDatabaseName("ux_positions_one_board_seat_per_person")
            .HasFilter("kind = 'BoardSeat' AND holder_id IS NOT NULL");
        b.HasOne<Person>().WithMany().HasForeignKey(p => p.HolderId).OnDelete(DeleteBehavior.Restrict);

        var seededAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        b.HasData(BoardSeats.Select(s => new
        {
            s.Id,
            Kind = PositionKind.BoardSeat,
            s.Title,
            SortOrder = s.Sort,
            IsActive = true,
            HolderId = (Guid?)null,
            HolderSince = (DateTimeOffset?)null,
            CreatedAt = seededAt,
        }));
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
