using Htn.Domain.Events;
using Htn.Domain.Files;
using Htn.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Htn.Infrastructure.Data.Configurations;

internal sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public const string RowVersion = "RowVersion";

    public void Configure(EntityTypeBuilder<Event> b)
    {
        b.ToTable("events", t =>
        {
            // Ended is derived from ends_at and never stored.
            t.HasCheckConstraint("ck_events_status", CheckConstraints.In("status",
                nameof(EventStatus.Draft), nameof(EventStatus.OnSale), nameof(EventStatus.Closed)));
            t.HasCheckConstraint("ck_events_proof_mode", CheckConstraints.In("proof_mode", Enum.GetNames<ProofMode>()));
            t.HasCheckConstraint("ck_events_slug",
                $"slug ~ '{EventSlug.Pattern}' AND char_length(slug) BETWEEN {EventSlug.MinLength} AND {EventSlug.MaxLength}");
            t.HasCheckConstraint("ck_events_period", "ends_at > starts_at");
            // The task reference counter only ever goes up (NextTaskNumber under the event row lock).
            t.HasCheckConstraint("ck_events_task_counter", "task_counter >= 0");
            t.HasCheckConstraint("ck_events_capacity", $"capacity BETWEEN {Event.MinCapacity} AND {Event.MaxCapacity}");
            t.HasCheckConstraint("ck_events_membership",
                $"(is_main_event AND membership_fee BETWEEN 0 AND {Event.MaxMembershipFee} " +
                $"AND membership_year BETWEEN {Event.MinMembershipYear} AND {Event.MaxMembershipYear}) " +
                "OR (NOT is_main_event AND membership_fee IS NULL AND membership_year IS NULL)");
            t.HasCheckConstraint("ck_events_email_accent_color", "email_accent_color IS NULL OR email_accent_color ~ '^#[0-9A-F]{6}$'");
            t.HasCheckConstraint("ck_events_email_payment_accounts",
                $"array_position(email_payment_account_ids, NULL) IS NULL AND cardinality(email_payment_account_ids) <= {Event.MaxEmailPaymentAccounts}");
            t.HasCheckConstraint("ck_events_payment_deadline",
                $"payment_deadline_hours IS NULL OR payment_deadline_hours BETWEEN {OrgSettings.MinPaymentDeadlineHours} AND {OrgSettings.MaxPaymentDeadlineHours}");
        });
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Slug).HasMaxLength(EventSlug.MaxLength).IsRequired();
        b.Property(e => e.Name).HasMaxLength(Event.NameMaxLength).IsRequired();
        b.Property(e => e.Venue).HasMaxLength(Event.VenueMaxLength).IsRequired();
        b.Property(e => e.Description).HasMaxLength(Event.DescriptionMaxLength);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.ProofMode).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.EmailAccentColor).HasMaxLength(7).IsFixedLength();
        b.Property(e => e.EmailPaymentAccountIds).HasColumnType("uuid[]").HasDefaultValueSql("'{}'::uuid[]").IsRequired();
        // Two admins publishing/editing the same event at once: the second save gets 409 concurrent_change.
        b.Property<uint>(RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.Ignore(e => e.CanChangeSlug);

        b.HasIndex(e => e.Slug).IsUnique();
        b.HasIndex(e => new { e.Status, e.StartsAt });
        b.HasIndex(e => e.EndsAt);
        // One main event per membership year.
        b.HasIndex(e => e.MembershipYear, "ux_events_main_event_year").IsUnique().HasDatabaseName("ux_events_main_event_year")
            .HasFilter("is_main_event");
        b.HasOne<StoredFile>().WithMany().HasForeignKey(e => e.CoverFileId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TicketTypeConfiguration : IEntityTypeConfiguration<TicketType>
{
    public void Configure(EntityTypeBuilder<TicketType> b)
    {
        b.ToTable("ticket_types", t =>
        {
            t.HasCheckConstraint("ck_ticket_types_visibility", CheckConstraints.In("visibility", Enum.GetNames<TicketVisibility>()));
            t.HasCheckConstraint("ck_ticket_types_price", $"price BETWEEN 0 AND {TicketType.MaxPrice}");
            t.HasCheckConstraint("ck_ticket_types_quantity", $"quantity BETWEEN {TicketType.MinQuantity} AND {TicketType.MaxQuantity}");
            t.HasCheckConstraint("ck_ticket_types_group_size",
                $"group_size BETWEEN {TicketType.MinGroupSize} AND {TicketType.MaxGroupSize} AND (visibility = 'Hidden' OR group_size <= {TicketType.MaxPublicGroupSize})");
            t.HasCheckConstraint("ck_ticket_types_sales_window",
                "sales_starts_at IS NULL OR sales_ends_at IS NULL OR sales_ends_at > sales_starts_at");
            t.HasCheckConstraint("ck_ticket_types_deactivated", "is_active = (deactivated_at IS NULL)");
        });
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).ValueGeneratedNever();
        b.Property(t => t.Name).HasMaxLength(TicketType.NameMaxLength).IsRequired();
        b.Property(t => t.Visibility).HasConversion<string>().HasMaxLength(16);
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.HasAlternateKey(t => new { t.Id, t.EventId }).HasName("ak_ticket_types_id_event_id");
        b.HasIndex(t => new { t.EventId, t.SortOrder });
        b.HasOne<Event>().WithMany().HasForeignKey(t => t.EventId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class VoucherConfiguration : IEntityTypeConfiguration<Voucher>
{
    public void Configure(EntityTypeBuilder<Voucher> b)
    {
        b.ToTable("vouchers", t =>
        {
            t.HasCheckConstraint("ck_vouchers_kind", CheckConstraints.In("kind", Enum.GetNames<VoucherKind>()));
            t.HasCheckConstraint("ck_vouchers_source", CheckConstraints.In("source", Enum.GetNames<VoucherSource>()));
            t.HasCheckConstraint("ck_vouchers_code", $"code ~ '{Voucher.CodePattern}'");
            t.HasCheckConstraint("ck_vouchers_value",
                $"(kind = 'Percent' AND percent_off BETWEEN {Voucher.MinPercent} AND {Voucher.MaxPercent} AND amount_off IS NULL) " +
                $"OR (kind = 'Fixed' AND amount_off > 0 AND amount_off <= {Voucher.MaxAmount} AND percent_off IS NULL)");
            t.HasCheckConstraint("ck_vouchers_usage_limit", $"usage_limit IS NULL OR usage_limit BETWEEN 1 AND {Voucher.MaxUsageLimit}");
            t.HasCheckConstraint("ck_vouchers_deactivated", "is_active = (deactivated_at IS NULL)");
            t.HasCheckConstraint("ck_vouchers_ticket_type_ids",
                $"array_position(ticket_type_ids, NULL) IS NULL AND cardinality(ticket_type_ids) <= {Voucher.MaxTicketScope}");
        });
        b.HasKey(v => v.Id);
        b.Property(v => v.Id).ValueGeneratedNever();
        b.Property(v => v.Code).HasMaxLength(Voucher.CodeMaxLength).IsRequired();
        b.Property(v => v.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(v => v.Source).HasConversion<string>().HasMaxLength(20);
        // A percentage, not money.
        b.Property(v => v.PercentOff).HasPrecision(5, 2);
        b.Property(v => v.TicketTypeIds).HasColumnType("uuid[]").HasDefaultValueSql("'{}'::uuid[]").IsRequired();
        b.Ignore(v => v.Value);
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.HasAlternateKey(v => new { v.Id, v.EventId }).HasName("ak_vouchers_id_event_id");
        // Codes are stored upper-case (ck_vouchers_code), so a plain unique index is case-insensitive. Also serves event_id lookups.
        b.HasIndex(v => new { v.EventId, v.Code }).IsUnique().HasDatabaseName("ux_vouchers_event_code");
        b.HasOne<Event>().WithMany().HasForeignKey(v => v.EventId).OnDelete(DeleteBehavior.Restrict);
    }
}
