using Htn.Domain.Events;
using Htn.Domain.Files;
using Htn.Domain.Registrations;
using Htn.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Htn.Infrastructure.Data.Configurations;

internal sealed class RegistrationConfiguration : IEntityTypeConfiguration<Registration>
{
    public const string IdempotencyIndex = "ux_registrations_event_idempotency_key";
    public const string ProofTokenIndex = "ux_registrations_proof_token_hash";
    public const string UsageIndex = "ix_registrations_usage";

    public void Configure(EntityTypeBuilder<Registration> b)
    {
        b.ToTable("registrations", t =>
        {
            t.HasCheckConstraint("ck_registrations_status", CheckConstraints.In("status", Enum.GetNames<RegistrationStatus>()));
            t.HasCheckConstraint("ck_registrations_source", CheckConstraints.In("source", Enum.GetNames<RegistrationSource>()));
            t.HasCheckConstraint("ck_registrations_seats", $"seats BETWEEN {TicketType.MinGroupSize} AND {TicketType.MaxGroupSize}");
            t.HasCheckConstraint("ck_registrations_amounts",
                "subtotal >= 0 AND discount >= 0 AND discount <= subtotal AND total = subtotal - discount");
            t.HasCheckConstraint("ck_registrations_voucher", "(voucher_id IS NULL) = (voucher_code IS NULL)");
            t.HasCheckConstraint("ck_registrations_cancelled",
                "(status = 'Cancelled') = (cancelled_at IS NOT NULL) AND (cancelled_at IS NULL) = (cancel_reason IS NULL)");
            // Cancelled keeps paid_at when a paid registration is cancelled (the payment stays recorded).
            t.HasCheckConstraint("ck_registrations_paid", "status = 'Cancelled' OR (status IN ('Paid', 'CheckedIn')) = (paid_at IS NOT NULL)");
            t.HasCheckConstraint("ck_registrations_deadline", "(total = 0) = (deadline IS NULL) AND (deadline IS NULL OR deadline > created_at)");
            t.HasCheckConstraint("ck_registrations_consent", "char_length(consent_version) > 0");
            // checked_in_at is the first arrival of the checkout; a registration never leaves CheckedIn.
            t.HasCheckConstraint("ck_registrations_checked_in", "(status = 'CheckedIn') = (checked_in_at IS NOT NULL)");
            t.HasCheckConstraint("ck_registrations_manual_actor", "source = 'Public' OR created_by_id IS NOT NULL");
        });
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.Source).HasConversion<string>().HasMaxLength(16);
        b.Property(r => r.VoucherCode).HasMaxLength(Voucher.CodeMaxLength);
        b.Property(r => r.ConsentVersion).HasMaxLength(40).IsRequired();
        b.Property(r => r.ProofTokenHash).HasMaxLength(64).IsFixedLength();
        b.Property(r => r.CancelReason).HasMaxLength(RegistrationForm.ReasonMaxLength);
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.Ignore(r => r.Buyer);
        b.Ignore(r => r.IsActive);

        // (id, event_id) is the target of attendees' composite foreign key, so an attendee can't point at another event.
        b.HasAlternateKey(r => new { r.Id, r.EventId }).HasName("ak_registrations_id_event_id");
        b.HasMany(r => r.Attendees).WithOne().HasForeignKey(a => new { a.RegistrationId, a.EventId }).HasPrincipalKey(r => new { r.Id, r.EventId })
            .OnDelete(DeleteBehavior.Restrict);
        b.Navigation(r => r.Attendees).AutoInclude();

        // Tabs and counts: WHERE event_id = ? [AND status = ?]; overdue adds deadline.
        b.HasIndex(r => new { r.EventId, r.Status, r.Deadline });
        // Admin list order (newest first) with a stable tie-break.
        b.HasIndex(r => new { r.EventId, r.CreatedAt, r.Id });
        // EventUsage: seats / ticket / voucher counts per event, index-only for every registration that may hold a seat.
        b.HasIndex(r => r.EventId).HasDatabaseName(UsageIndex)
            .IncludeProperties(r => new { r.TicketTypeId, r.VoucherId, r.Seats, r.Status, r.Deadline })
            .HasFilter("status <> 'Cancelled'");
        b.HasIndex(r => new { r.EventId, r.IdempotencyKey }).IsUnique().HasDatabaseName(IdempotencyIndex)
            .HasFilter("idempotency_key IS NOT NULL");
        b.HasIndex(r => r.ProofTokenHash).IsUnique().HasDatabaseName(ProofTokenIndex).HasFilter("proof_token_hash IS NOT NULL");

        b.HasOne<Event>().WithMany().HasForeignKey(r => r.EventId).OnDelete(DeleteBehavior.Restrict);
        // Composite foreign keys: the ticket type and voucher must belong to the registration's own event.
        b.HasOne<TicketType>().WithMany().HasForeignKey(r => new { r.TicketTypeId, r.EventId }).HasPrincipalKey(t => new { t.Id, t.EventId })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Voucher>().WithMany().HasForeignKey(r => new { r.VoucherId, r.EventId }).HasPrincipalKey(v => new { v.Id, v.EventId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AttendeeConfiguration : IEntityTypeConfiguration<Attendee>
{
    public const string ParticipantIdIndex = "ux_attendees_event_participant_id";

    public void Configure(EntityTypeBuilder<Attendee> b)
    {
        b.ToTable("attendees", t =>
        {
            // HTN- + two groups of four from PublicIdGenerator.Alphabet.
            t.HasCheckConstraint("ck_attendees_participant_id", "participant_id ~ '^HTN-[2-9A-HJKMNP-TV-Z]{4}-[2-9A-HJKMNP-TV-Z]{4}$'");
            t.HasCheckConstraint("ck_attendees_mobile", $"mobile IS NULL OR mobile ~ '{MobileNumber.Pattern}'");
            t.HasCheckConstraint("ck_attendees_shirt_size", "shirt_size IS NULL OR " + CheckConstraints.In("shirt_size", [.. RegistrationForm.ShirtSizes]));
            t.HasCheckConstraint("ck_attendees_gender", "gender IS NULL OR " + CheckConstraints.In("gender", [.. RegistrationForm.Genders]));
            t.HasCheckConstraint("ck_attendees_hear_about", "hear_about IS NULL OR " + CheckConstraints.In("hear_about", [.. RegistrationForm.HearAbout]));
            t.HasCheckConstraint("ck_attendees_ticket_version", "ticket_version >= 1");
            t.HasCheckConstraint("ck_attendees_sort_order", $"sort_order BETWEEN 0 AND {TicketType.MaxGroupSize - 1}");
            t.HasCheckConstraint("ck_attendees_checked_in",
                "(checked_in_at IS NULL) = (checked_in_station IS NULL) AND (checked_in_at IS NULL) = (checked_in_by_id IS NULL)");
        });
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedNever();
        // Target of check_in_scans' composite foreign key (attendee_id, event_id).
        b.HasAlternateKey(a => new { a.Id, a.EventId }).HasName("ak_attendees_id_event_id");
        b.Property(a => a.ParticipantId).HasMaxLength(13).IsFixedLength().IsRequired();
        b.Property(a => a.FirstName).HasMaxLength(RegistrationForm.NameMaxLength).IsRequired();
        b.Property(a => a.MiddleName).HasMaxLength(RegistrationForm.NameMaxLength);
        b.Property(a => a.LastName).HasMaxLength(RegistrationForm.NameMaxLength).IsRequired();
        b.Property(a => a.Gender).HasMaxLength(30);
        b.Property(a => a.Email).HasMaxLength(RegistrationForm.EmailMaxLength).IsRequired();
        b.Property(a => a.Mobile).HasMaxLength(13);
        b.Property(a => a.Occupation).HasMaxLength(RegistrationForm.OccupationMaxLength);
        b.Property(a => a.Company).HasMaxLength(RegistrationForm.CompanyMaxLength);
        b.Property(a => a.ShirtSize).HasMaxLength(4);
        b.Property(a => a.HearAbout).HasMaxLength(40);
        b.Property(a => a.CheckedInStation).HasMaxLength(60);
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.Ignore(a => a.IsBuyer);
        b.Ignore(a => a.FullName);

        b.HasIndex(a => new { a.EventId, a.ParticipantId }).IsUnique().HasDatabaseName(ParticipantIdIndex);
        b.HasIndex(a => new { a.RegistrationId, a.SortOrder }).IsUnique();
        // Desk lookup by participant ID across events (3C) and lower(email) for search / member matching: raw SQL in the migration.
        b.HasIndex(a => a.ParticipantId);
        // Check-in stats: checked in per event and per station (3C). Member matching by lower(email) among checked-in
        // attendees is a partial expression index in the CheckIn migration.
        b.HasIndex(a => new { a.EventId, a.CheckedInStation }).HasFilter("checked_in_at IS NOT NULL");
        b.HasOne<Event>().WithMany().HasForeignKey(a => a.EventId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public const string OpenPaymentIndex = "ux_payments_registration_open";

    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments", t =>
        {
            t.HasCheckConstraint("ck_payments_method", CheckConstraints.In("method", Enum.GetNames<PaymentMethod>()));
            t.HasCheckConstraint("ck_payments_amount", "amount >= 0");
            t.HasCheckConstraint("ck_payments_comp", "(method = 'Comp') = (amount = 0) AND (method <> 'Comp' OR payment_account_id IS NULL)");
            t.HasCheckConstraint("ck_payments_reverted",
                "(reverted_at IS NULL) = (revert_reason IS NULL) AND (reverted_at IS NULL OR reverted_at >= marked_at)");
        });
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();
        b.Property(p => p.Method).HasConversion<string>().HasMaxLength(16);
        b.Property(p => p.Reference).HasMaxLength(Payment.ReferenceMaxLength);
        b.Property(p => p.RevertReason).HasMaxLength(RegistrationForm.ReasonMaxLength);
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.Ignore(p => p.IsReverted);
        // At most one payment that is not reverted per registration.
        b.HasIndex(p => p.RegistrationId).IsUnique().HasDatabaseName(OpenPaymentIndex).HasFilter("reverted_at IS NULL");
        b.HasIndex(p => new { p.RegistrationId, p.MarkedAt });
        b.HasIndex(p => p.MarkedAt);
        b.HasOne<Registration>().WithMany().HasForeignKey(p => p.RegistrationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PaymentAccount>().WithMany().HasForeignKey(p => p.PaymentAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PaymentProofConfiguration : IEntityTypeConfiguration<PaymentProof>
{
    public void Configure(EntityTypeBuilder<PaymentProof> b)
    {
        b.ToTable("payment_proofs");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();
        b.Property(p => p.Reference).HasMaxLength(RegistrationForm.ReferenceMaxLength);
        b.HasIndex(p => new { p.RegistrationId, p.UploadedAt });
        b.HasIndex(p => p.FileId).IsUnique();
        b.HasOne<Registration>().WithMany().HasForeignKey(p => p.RegistrationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoredFile>().WithMany().HasForeignKey(p => p.FileId).OnDelete(DeleteBehavior.Restrict);
    }
}
