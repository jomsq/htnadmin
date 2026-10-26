using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CheckIn_Guards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_check_in_scans_attendees_attendee_id",
                table: "check_in_scans");

            migrationBuilder.DropIndex(
                name: "ix_check_in_scans_attendee_id",
                table: "check_in_scans");

            migrationBuilder.DropCheckConstraint(
                name: "ck_check_in_scans_result",
                table: "check_in_scans");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_attendees_id_event_id",
                table: "attendees",
                columns: new[] { "id", "event_id" });

            migrationBuilder.CreateIndex(
                name: "ix_check_in_scans_attendee_id_event_id",
                table: "check_in_scans",
                columns: new[] { "attendee_id", "event_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_in_scans_result",
                table: "check_in_scans",
                sql: "result IN ('Welcome', 'AlreadyCheckedIn', 'NotPaid', 'Cancelled', 'Invalid', 'Lookup')");

            migrationBuilder.AddForeignKey(
                name: "fk_check_in_scans_attendees_attendee_id_event_id",
                table: "check_in_scans",
                columns: new[] { "attendee_id", "event_id" },
                principalTable: "attendees",
                principalColumns: new[] { "id", "event_id" },
                onDelete: ReferentialAction.Restrict);

            // Check-in records are write-once (L5). Same trigger functions as Registrations_IntegrityGuards, extended.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION htn_attendees_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'attendees are never deleted' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF (NEW.registration_id, NEW.event_id, NEW.participant_id) IS DISTINCT FROM (OLD.registration_id, OLD.event_id, OLD.participant_id) THEN
                        RAISE EXCEPTION 'attendees: registration, event and participant ID are immutable' USING ERRCODE = 'check_violation';
                    END IF;
                    -- A check-in is recorded once (NULL -> value) and never edited or cleared.
                    IF OLD.checked_in_at IS NOT NULL
                       AND (NEW.checked_in_at, NEW.checked_in_station, NEW.checked_in_by_id)
                           IS DISTINCT FROM (OLD.checked_in_at, OLD.checked_in_station, OLD.checked_in_by_id) THEN
                        RAISE EXCEPTION 'attendees: a check-in cannot be changed' USING ERRCODE = 'check_violation';
                    END IF;
                    -- Older QR codes must stay invalid.
                    IF NEW.ticket_version < OLD.ticket_version THEN
                        RAISE EXCEPTION 'attendees: ticket_version never decreases' USING ERRCODE = 'check_violation';
                    END IF;
                    -- The Member rule matches check-ins by email: frozen once checked in.
                    IF OLD.checked_in_at IS NOT NULL AND NEW.email IS DISTINCT FROM OLD.email THEN
                        RAISE EXCEPTION 'attendees: email is frozen after check-in' USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE OR REPLACE FUNCTION htn_registrations_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'registrations are never deleted (cancel with a reason)' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF (NEW.event_id, NEW.ticket_type_id, NEW.seats, NEW.voucher_id, NEW.voucher_code, NEW.subtotal, NEW.discount,
                        NEW.total, NEW.created_at, NEW.consent_at, NEW.consent_version, NEW.source, NEW.idempotency_key)
                       IS DISTINCT FROM
                       (OLD.event_id, OLD.ticket_type_id, OLD.seats, OLD.voucher_id, OLD.voucher_code, OLD.subtotal, OLD.discount,
                        OLD.total, OLD.created_at, OLD.consent_at, OLD.consent_version, OLD.source, OLD.idempotency_key) THEN
                        RAISE EXCEPTION 'registrations: the checkout terms are immutable' USING ERRCODE = 'check_violation';
                    END IF;
                    -- The first arrival of the checkout is recorded once.
                    IF OLD.checked_in_at IS NOT NULL AND NEW.checked_in_at IS DISTINCT FROM OLD.checked_in_at THEN
                        RAISE EXCEPTION 'registrations: checked_in_at cannot be changed' USING ERRCODE = 'check_violation';
                    END IF;
                    -- PendingPayment -> Paid | Cancelled; Paid -> PendingPayment (revert) | CheckedIn | Cancelled. CheckedIn and
                    -- Cancelled are final, and nothing goes straight from PendingPayment to CheckedIn.
                    IF NEW.status IS DISTINCT FROM OLD.status AND NOT (
                           (OLD.status = 'PendingPayment' AND NEW.status IN ('Paid', 'Cancelled'))
                        OR (OLD.status = 'Paid' AND NEW.status IN ('PendingPayment', 'CheckedIn', 'Cancelled'))) THEN
                        RAISE EXCEPTION 'registrations: status % -> % is not allowed', OLD.status, NEW.status USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to the Registrations_IntegrityGuards versions of the trigger functions.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION htn_attendees_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'attendees are never deleted' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF (NEW.registration_id, NEW.event_id, NEW.participant_id) IS DISTINCT FROM (OLD.registration_id, OLD.event_id, OLD.participant_id) THEN
                        RAISE EXCEPTION 'attendees: registration, event and participant ID are immutable' USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE OR REPLACE FUNCTION htn_registrations_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'registrations are never deleted (cancel with a reason)' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF (NEW.event_id, NEW.ticket_type_id, NEW.seats, NEW.voucher_id, NEW.voucher_code, NEW.subtotal, NEW.discount,
                        NEW.total, NEW.created_at, NEW.consent_at, NEW.consent_version, NEW.source, NEW.idempotency_key)
                       IS DISTINCT FROM
                       (OLD.event_id, OLD.ticket_type_id, OLD.seats, OLD.voucher_id, OLD.voucher_code, OLD.subtotal, OLD.discount,
                        OLD.total, OLD.created_at, OLD.consent_at, OLD.consent_version, OLD.source, OLD.idempotency_key) THEN
                        RAISE EXCEPTION 'registrations: the checkout terms are immutable' USING ERRCODE = 'check_violation';
                    END IF;
                    -- PendingPayment -> Paid | Cancelled; Paid -> PendingPayment (revert) | CheckedIn | Cancelled. CheckedIn and
                    -- Cancelled are final, and nothing goes straight from PendingPayment to CheckedIn.
                    IF NEW.status IS DISTINCT FROM OLD.status AND NOT (
                           (OLD.status = 'PendingPayment' AND NEW.status IN ('Paid', 'Cancelled'))
                        OR (OLD.status = 'Paid' AND NEW.status IN ('PendingPayment', 'CheckedIn', 'Cancelled'))) THEN
                        RAISE EXCEPTION 'registrations: status % -> % is not allowed', OLD.status, NEW.status USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_check_in_scans_attendees_attendee_id_event_id",
                table: "check_in_scans");

            migrationBuilder.DropIndex(
                name: "ix_check_in_scans_attendee_id_event_id",
                table: "check_in_scans");

            migrationBuilder.DropCheckConstraint(
                name: "ck_check_in_scans_result",
                table: "check_in_scans");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_attendees_id_event_id",
                table: "attendees");

            migrationBuilder.CreateIndex(
                name: "ix_check_in_scans_attendee_id",
                table: "check_in_scans",
                column: "attendee_id",
                filter: "attendee_id IS NOT NULL");

            // NOT VALID: Lookup rows stay in the append-only scan log; only new rows are checked.
            migrationBuilder.Sql("ALTER TABLE check_in_scans ADD CONSTRAINT ck_check_in_scans_result CHECK (result IN ('Welcome', 'AlreadyCheckedIn', 'NotPaid', 'Cancelled', 'Invalid')) NOT VALID;");

            migrationBuilder.AddForeignKey(
                name: "fk_check_in_scans_attendees_attendee_id",
                table: "check_in_scans",
                column: "attendee_id",
                principalTable: "attendees",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
