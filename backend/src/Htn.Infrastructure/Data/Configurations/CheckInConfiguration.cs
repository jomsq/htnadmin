using Htn.Domain.CheckIn;
using Htn.Domain.Events;
using Htn.Domain.Registrations;
using Htn.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Htn.Infrastructure.Data.Configurations;

internal sealed class CheckInScanConfiguration : IEntityTypeConfiguration<CheckInScan>
{
    public void Configure(EntityTypeBuilder<CheckInScan> b)
    {
        b.ToTable("check_in_scans", t =>
        {
            t.HasCheckConstraint("ck_check_in_scans_result", CheckConstraints.In("result", Enum.GetNames<CheckInResult>()));
            t.HasCheckConstraint("ck_check_in_scans_method", CheckConstraints.In("method", Enum.GetNames<CheckInMethod>()));
            t.HasCheckConstraint("ck_check_in_scans_station",
                $"char_length(station) BETWEEN 1 AND {CheckInStation.MaxLength} AND station = btrim(station)");
            t.HasCheckConstraint("ck_check_in_scans_attendee", "attendee_id IS NOT NULL OR result = 'Invalid'");
        });
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).ValueGeneratedNever();
        b.Property(s => s.Result).HasConversion<string>().HasMaxLength(20);
        b.Property(s => s.Method).HasConversion<string>().HasMaxLength(10);
        b.Property(s => s.Station).HasMaxLength(CheckInStation.MaxLength).IsRequired();

        // Recent scans (newest first) and stations online (scans in the last 5 minutes) per event.
        b.HasIndex(s => new { s.EventId, s.At });
        // Recent scans filtered to one station.
        b.HasIndex(s => new { s.EventId, s.Station, s.At });
        b.HasIndex(s => s.StaffUserId);

        b.HasOne<Event>().WithMany().HasForeignKey(s => s.EventId).OnDelete(DeleteBehavior.Restrict);
        // Composite: a scan's attendee must belong to the scan's event.
        b.HasOne<Attendee>().WithMany().HasForeignKey(s => new { s.AttendeeId, s.EventId }).HasPrincipalKey(a => new { a.Id, a.EventId })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(s => s.StaffUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
