using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Registrations_IntegrityGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_attendees_registrations_registration_id",
                table: "attendees");

            migrationBuilder.DropForeignKey(
                name: "fk_registrations_ticket_types_ticket_type_id",
                table: "registrations");

            migrationBuilder.DropForeignKey(
                name: "fk_registrations_vouchers_voucher_id",
                table: "registrations");

            migrationBuilder.DropIndex(
                name: "ix_vouchers_event_id",
                table: "vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ticket_types_group_size",
                table: "ticket_types");

            migrationBuilder.DropIndex(
                name: "ix_registrations_event_id_created_at",
                table: "registrations");

            migrationBuilder.DropIndex(
                name: "ix_registrations_ticket_type_id_status",
                table: "registrations");

            migrationBuilder.DropIndex(
                name: "ix_registrations_voucher_id",
                table: "registrations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_registrations_checked_in",
                table: "registrations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_registrations_deadline",
                table: "registrations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_registrations_paid",
                table: "registrations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_comp",
                table: "payments");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_vouchers_id_event_id",
                table: "vouchers",
                columns: new[] { "id", "event_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_ticket_types_id_event_id",
                table: "ticket_types",
                columns: new[] { "id", "event_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_registrations_id_event_id",
                table: "registrations",
                columns: new[] { "id", "event_id" });

            // The (event_id, upper(code)) expression index from the Events migration becomes a plain unique index:
            // codes are already stored upper-case (ck_vouchers_code), so it is still case-insensitive.
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_vouchers_event_code;");

            migrationBuilder.CreateIndex(
                name: "ux_vouchers_event_code",
                table: "vouchers",
                columns: new[] { "event_id", "code" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE vouchers ADD CONSTRAINT ck_vouchers_ticket_type_ids CHECK (array_position(ticket_type_ids, NULL) IS NULL AND cardinality(ticket_type_ids) <= 50) NOT VALID;
                ALTER TABLE vouchers VALIDATE CONSTRAINT ck_vouchers_ticket_type_ids;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE ticket_types ADD CONSTRAINT ck_ticket_types_group_size CHECK (group_size BETWEEN 1 AND 50 AND (visibility = 'Hidden' OR group_size <= 20)) NOT VALID;
                ALTER TABLE ticket_types VALIDATE CONSTRAINT ck_ticket_types_group_size;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_registrations_event_id_created_at_id",
                table: "registrations",
                columns: new[] { "event_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_registrations_ticket_type_id_event_id",
                table: "registrations",
                columns: new[] { "ticket_type_id", "event_id" });

            migrationBuilder.CreateIndex(
                name: "ix_registrations_usage",
                table: "registrations",
                column: "event_id",
                filter: "status <> 'Cancelled'")
                .Annotation("Npgsql:IndexInclude", new[] { "ticket_type_id", "voucher_id", "seats", "status", "deadline" });

            migrationBuilder.CreateIndex(
                name: "ix_registrations_voucher_id_event_id",
                table: "registrations",
                columns: new[] { "voucher_id", "event_id" });

            migrationBuilder.Sql("""
                ALTER TABLE registrations ADD CONSTRAINT ck_registrations_checked_in CHECK ((status = 'CheckedIn') = (checked_in_at IS NOT NULL)) NOT VALID;
                ALTER TABLE registrations VALIDATE CONSTRAINT ck_registrations_checked_in;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE registrations ADD CONSTRAINT ck_registrations_deadline CHECK ((total = 0) = (deadline IS NULL) AND (deadline IS NULL OR deadline > created_at)) NOT VALID;
                ALTER TABLE registrations VALIDATE CONSTRAINT ck_registrations_deadline;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE registrations ADD CONSTRAINT ck_registrations_paid CHECK (status = 'Cancelled' OR (status IN ('Paid', 'CheckedIn')) = (paid_at IS NOT NULL)) NOT VALID;
                ALTER TABLE registrations VALIDATE CONSTRAINT ck_registrations_paid;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE payments ADD CONSTRAINT ck_payments_comp CHECK ((method = 'Comp') = (amount = 0) AND (method <> 'Comp' OR payment_account_id IS NULL)) NOT VALID;
                ALTER TABLE payments VALIDATE CONSTRAINT ck_payments_comp;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE events ADD CONSTRAINT ck_events_email_payment_accounts CHECK (array_position(email_payment_account_ids, NULL) IS NULL AND cardinality(email_payment_account_ids) <= 10) NOT VALID;
                ALTER TABLE events VALIDATE CONSTRAINT ck_events_email_payment_accounts;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_attendees_registration_id_event_id",
                table: "attendees",
                columns: new[] { "registration_id", "event_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_attendees_registrations_registration_id_event_id",
                table: "attendees",
                columns: new[] { "registration_id", "event_id" },
                principalTable: "registrations",
                principalColumns: new[] { "id", "event_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_registrations_ticket_types_ticket_type_id_event_id",
                table: "registrations",
                columns: new[] { "ticket_type_id", "event_id" },
                principalTable: "ticket_types",
                principalColumns: new[] { "id", "event_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_registrations_vouchers_voucher_id_event_id",
                table: "registrations",
                columns: new[] { "voucher_id", "event_id" },
                principalTable: "vouchers",
                principalColumns: new[] { "id", "event_id" },
                onDelete: ReferentialAction.Restrict);

            // Existing rows were checked against every constraint above (NOT VALID, then VALIDATE) before this point;
            // the dev seed data and anything the API can write pass them.

            // Database guards (defence in depth under the API's own rules). htn_reject_change() is from the Initial migration.
            migrationBuilder.Sql("""
                CREATE FUNCTION htn_registrations_guard() RETURNS trigger LANGUAGE plpgsql AS $$
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
                CREATE TRIGGER registrations_guard BEFORE UPDATE OR DELETE ON registrations
                    FOR EACH ROW EXECUTE FUNCTION htn_registrations_guard();

                CREATE FUNCTION htn_attendees_guard() RETURNS trigger LANGUAGE plpgsql AS $$
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
                CREATE TRIGGER attendees_guard BEFORE UPDATE OR DELETE ON attendees
                    FOR EACH ROW EXECUTE FUNCTION htn_attendees_guard();

                -- Payments: inserted unreverted, for the registration's total (or as a Comp), then at most one revert.
                CREATE FUNCTION htn_payments_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'payments are never deleted (revert with a reason)' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF TG_OP = 'INSERT' THEN
                        IF NEW.reverted_at IS NOT NULL OR NEW.reverted_by_id IS NOT NULL OR NEW.revert_reason IS NOT NULL THEN
                            RAISE EXCEPTION 'payments: a new payment cannot be reverted already' USING ERRCODE = 'check_violation';
                        END IF;
                        IF NEW.method <> 'Comp'
                           AND NEW.amount IS DISTINCT FROM (SELECT r.total FROM registrations r WHERE r.id = NEW.registration_id) THEN
                            RAISE EXCEPTION 'payments: amount must equal the registration total' USING ERRCODE = 'check_violation';
                        END IF;
                        RETURN NEW;
                    END IF;
                    IF OLD.reverted_at IS NOT NULL OR NEW.reverted_at IS NULL
                       OR (NEW.id, NEW.registration_id, NEW.method, NEW.payment_account_id, NEW.reference, NEW.amount, NEW.received_at,
                           NEW.marked_at, NEW.marked_by_id)
                          IS DISTINCT FROM
                          (OLD.id, OLD.registration_id, OLD.method, OLD.payment_account_id, OLD.reference, OLD.amount, OLD.received_at,
                           OLD.marked_at, OLD.marked_by_id) THEN
                        RAISE EXCEPTION 'payments: only a single revert is allowed' USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER payments_guard BEFORE INSERT OR UPDATE OR DELETE ON payments
                    FOR EACH ROW EXECUTE FUNCTION htn_payments_guard();
                CREATE TRIGGER payments_no_truncate BEFORE TRUNCATE ON payments
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();
                """);

            // TODO(prod): run the API as the non-owner role htn_app (see README "Database role"). The REVOKEs in these
            // migrations apply to it; triggers apply to every role including the owner.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS payments_no_truncate ON payments;
                DROP TRIGGER IF EXISTS payments_guard ON payments;
                DROP TRIGGER IF EXISTS attendees_guard ON attendees;
                DROP TRIGGER IF EXISTS registrations_guard ON registrations;
                DROP FUNCTION IF EXISTS htn_payments_guard();
                DROP FUNCTION IF EXISTS htn_attendees_guard();
                DROP FUNCTION IF EXISTS htn_registrations_guard();
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_attendees_registrations_registration_id_event_id",
                table: "attendees");

            migrationBuilder.DropForeignKey(
                name: "fk_registrations_ticket_types_ticket_type_id_event_id",
                table: "registrations");

            migrationBuilder.DropForeignKey(
                name: "fk_registrations_vouchers_voucher_id_event_id",
                table: "registrations");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_vouchers_id_event_id",
                table: "vouchers");

            migrationBuilder.DropIndex(
                name: "ux_vouchers_event_code",
                table: "vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vouchers_ticket_type_ids",
                table: "vouchers");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_ticket_types_id_event_id",
                table: "ticket_types");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ticket_types_group_size",
                table: "ticket_types");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_registrations_id_event_id",
                table: "registrations");

            migrationBuilder.DropIndex(
                name: "ix_registrations_event_id_created_at_id",
                table: "registrations");

            migrationBuilder.DropIndex(
                name: "ix_registrations_ticket_type_id_event_id",
                table: "registrations");

            migrationBuilder.DropIndex(
                name: "ix_registrations_usage",
                table: "registrations");

            migrationBuilder.DropIndex(
                name: "ix_registrations_voucher_id_event_id",
                table: "registrations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_registrations_checked_in",
                table: "registrations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_registrations_deadline",
                table: "registrations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_registrations_paid",
                table: "registrations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_comp",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_events_email_payment_accounts",
                table: "events");

            migrationBuilder.DropIndex(
                name: "ix_attendees_registration_id_event_id",
                table: "attendees");

            migrationBuilder.CreateIndex(
                name: "ix_vouchers_event_id",
                table: "vouchers",
                column: "event_id");

            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_vouchers_event_code ON vouchers (event_id, upper(code));");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ticket_types_group_size",
                table: "ticket_types",
                sql: "group_size BETWEEN 1 AND 50");

            migrationBuilder.CreateIndex(
                name: "ix_registrations_event_id_created_at",
                table: "registrations",
                columns: new[] { "event_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_registrations_ticket_type_id_status",
                table: "registrations",
                columns: new[] { "ticket_type_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_registrations_voucher_id",
                table: "registrations",
                column: "voucher_id",
                filter: "voucher_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_registrations_checked_in",
                table: "registrations",
                sql: "status <> 'CheckedIn' OR checked_in_at IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_registrations_deadline",
                table: "registrations",
                sql: "total = 0 OR deadline IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_registrations_paid",
                table: "registrations",
                sql: "status NOT IN ('Paid', 'CheckedIn') OR paid_at IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_comp",
                table: "payments",
                sql: "method <> 'Comp' OR (amount = 0 AND payment_account_id IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_attendees_registrations_registration_id",
                table: "attendees",
                column: "registration_id",
                principalTable: "registrations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_registrations_ticket_types_ticket_type_id",
                table: "registrations",
                column: "ticket_type_id",
                principalTable: "ticket_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_registrations_vouchers_voucher_id",
                table: "registrations",
                column: "voucher_id",
                principalTable: "vouchers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
