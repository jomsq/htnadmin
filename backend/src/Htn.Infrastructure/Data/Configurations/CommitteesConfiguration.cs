using Htn.Domain.Committees;
using Htn.Domain.Events;
using Htn.Domain.Organization;
using Htn.Domain.Registrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Htn.Infrastructure.Data.Configurations;

internal sealed class CommitteeConfiguration : IEntityTypeConfiguration<Committee>
{
    /// <summary>Unique (event_id, lower(name)) — the Committees migration replaces this index with the lower() form.</summary>
    public const string NameIndex = "ux_committees_event_name";

    public void Configure(EntityTypeBuilder<Committee> b)
    {
        b.ToTable("committees", t =>
        {
            t.HasCheckConstraint("ck_committees_name",
                $"char_length(name) BETWEEN 1 AND {Committee.NameMaxLength} AND name = btrim(name)");
            t.HasCheckConstraint("ck_committees_sort_order", $"sort_order BETWEEN 0 AND {Committee.MaxSortOrder}");
            t.HasCheckConstraint("ck_committees_deactivated", "is_active = (deactivated_at IS NULL)");
            // A deactivated committee has no lead (Deactivate clears it).
            t.HasCheckConstraint("ck_committees_lead", "is_active OR lead_person_id IS NULL");
        });
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).ValueGeneratedNever();
        b.Property(c => c.Name).HasMaxLength(Committee.NameMaxLength).IsRequired();
        // Two managers renaming or re-leading the same committee at once: the second save gets 409 concurrent_change.
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();

        // Members and volunteers are keyed by (committee_id, event_id), so a child can't cross events.
        b.HasAlternateKey(c => new { c.Id, c.EventId }).HasName("ak_committees_id_event_id");
        b.HasIndex(c => new { c.EventId, c.SortOrder });
        b.HasIndex(c => new { c.EventId, c.Name }).IsUnique().HasDatabaseName(NameIndex);
        b.HasIndex(c => c.LeadPersonId).HasFilter("lead_person_id IS NOT NULL");

        b.HasOne<Event>().WithMany().HasForeignKey(c => c.EventId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(c => c.LeadPersonId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CommitteeMemberConfiguration : IEntityTypeConfiguration<CommitteeMember>
{
    /// <summary>One active committee per person per event.</summary>
    public const string ActivePersonIndex = "ux_committee_members_active_person";

    /// <summary>One active lead row per committee.</summary>
    public const string ActiveLeadIndex = "ux_committee_members_active_lead";

    public void Configure(EntityTypeBuilder<CommitteeMember> b)
    {
        b.ToTable("committee_members", t =>
        {
            t.HasCheckConstraint("ck_committee_members_role", CheckConstraints.In("role", Enum.GetNames<CommitteeRole>()));
            t.HasCheckConstraint("ck_committee_members_left", "left_at IS NULL OR left_at >= joined_at");
        });
        b.HasKey(m => m.Id);
        b.Property(m => m.Id).ValueGeneratedNever();
        b.Property(m => m.Role).HasConversion<string>().HasMaxLength(10);
        b.Ignore(m => m.IsActive);
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();

        b.HasIndex(m => new { m.EventId, m.PersonId }).IsUnique().HasDatabaseName(ActivePersonIndex)
            .HasFilter("left_at IS NULL");
        b.HasIndex(m => m.CommitteeId).IsUnique().HasDatabaseName(ActiveLeadIndex)
            .HasFilter("left_at IS NULL AND role = 'Lead'");
        // The committee card: active members, then the full history of one person.
        b.HasIndex(m => new { m.CommitteeId, m.JoinedAt });
        b.HasIndex(m => m.PersonId);

        b.HasOne<Committee>().WithMany().HasForeignKey(m => new { m.CommitteeId, m.EventId })
            .HasPrincipalKey(c => new { c.Id, c.EventId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(m => m.PersonId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EventVolunteerConfiguration : IEntityTypeConfiguration<EventVolunteer>
{
    public void Configure(EntityTypeBuilder<EventVolunteer> b)
    {
        b.ToTable("event_volunteers", t =>
        {
            t.HasCheckConstraint("ck_event_volunteers_name",
                $"char_length(name) BETWEEN 1 AND {EventVolunteer.NameMaxLength} AND name = btrim(name)");
            t.HasCheckConstraint("ck_event_volunteers_email",
                $"email IS NULL OR (char_length(email) BETWEEN 3 AND {EventVolunteer.EmailMaxLength} AND email = lower(email) AND email ~ '^[^@[:space:]]+@[^@[:space:]]+\\.[^@[:space:]]+$')");
            t.HasCheckConstraint("ck_event_volunteers_mobile", $"mobile IS NULL OR mobile ~ '{MobileNumber.Pattern}'");
            t.HasCheckConstraint("ck_event_volunteers_deactivated", "is_active = (deactivated_at IS NULL)");
            // A deactivated volunteer is off every committee (Deactivate detaches them).
            t.HasCheckConstraint("ck_event_volunteers_committee", "is_active OR committee_id IS NULL");
        });
        b.HasKey(v => v.Id);
        b.Property(v => v.Id).ValueGeneratedNever();
        b.Property(v => v.Name).HasMaxLength(EventVolunteer.NameMaxLength).IsRequired();
        b.Property(v => v.Email).HasMaxLength(EventVolunteer.EmailMaxLength);
        b.Property(v => v.Mobile).HasMaxLength(13);
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();

        // Task assignees are keyed by (volunteer_id, event_id), so a volunteer can't be put on another event's card.
        b.HasAlternateKey(v => new { v.Id, v.EventId }).HasName("ak_event_volunteers_id_event_id");

        // The event's volunteer pool, and the volunteers on one committee card.
        b.HasIndex(v => new { v.EventId, v.Name });
        b.HasIndex(v => v.CommitteeId).HasFilter("committee_id IS NOT NULL");

        b.HasOne<Event>().WithMany().HasForeignKey(v => v.EventId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Committee>().WithMany().HasForeignKey(v => new { v.CommitteeId, v.EventId })
            .HasPrincipalKey(c => new { c.Id, c.EventId }).OnDelete(DeleteBehavior.Restrict);
    }
}
