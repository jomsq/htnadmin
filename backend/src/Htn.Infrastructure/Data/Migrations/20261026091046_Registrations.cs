using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Registrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "registrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seats = table.Column<int>(type: "integer", nullable: false),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    voucher_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    subtotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consent_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: true),
                    proof_token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    checked_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registrations", x => x.id);
                    table.CheckConstraint("ck_registrations_amounts", "subtotal >= 0 AND discount >= 0 AND discount <= subtotal AND total = subtotal - discount");
                    table.CheckConstraint("ck_registrations_cancelled", "(status = 'Cancelled') = (cancelled_at IS NOT NULL) AND (cancelled_at IS NULL) = (cancel_reason IS NULL)");
                    table.CheckConstraint("ck_registrations_consent", "char_length(consent_version) > 0");
                    table.CheckConstraint("ck_registrations_deadline", "total = 0 OR deadline IS NOT NULL");
                    table.CheckConstraint("ck_registrations_manual_actor", "source = 'Public' OR created_by_id IS NOT NULL");
                    table.CheckConstraint("ck_registrations_paid", "status NOT IN ('Paid', 'CheckedIn') OR paid_at IS NOT NULL");
                    table.CheckConstraint("ck_registrations_seats", "seats BETWEEN 1 AND 50");
                    table.CheckConstraint("ck_registrations_source", "source IN ('Public', 'Manual')");
                    table.CheckConstraint("ck_registrations_status", "status IN ('PendingPayment', 'Paid', 'CheckedIn', 'Cancelled')");
                    table.CheckConstraint("ck_registrations_voucher", "(voucher_id IS NULL) = (voucher_code IS NULL)");
                    table.ForeignKey(
                        name: "fk_registrations_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_registrations_ticket_types_ticket_type_id",
                        column: x => x.ticket_type_id,
                        principalTable: "ticket_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_registrations_vouchers_voucher_id",
                        column: x => x.voucher_id,
                        principalTable: "vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attendees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    participant_id = table.Column<string>(type: "character(13)", fixedLength: true, maxLength: 13, nullable: false),
                    first_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    middle_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    last_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    gender = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    mobile = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    occupation = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    company = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    shirt_size = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    hear_about = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ticket_version = table.Column<int>(type: "integer", nullable: false),
                    checked_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    checked_in_station = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    checked_in_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendees", x => x.id);
                    table.CheckConstraint("ck_attendees_gender", "gender IS NULL OR gender IN ('Female', 'Male', 'Non-binary', 'Prefer to self-describe', 'Prefer not to say')");
                    table.CheckConstraint("ck_attendees_hear_about", "hear_about IS NULL OR hear_about IN ('Facebook', 'LinkedIn', 'Friend or colleague', 'School or organization', 'Sponsor', 'Other')");
                    table.CheckConstraint("ck_attendees_mobile", "mobile IS NULL OR mobile ~ '^\\+639[0-9]{9}$'");
                    table.CheckConstraint("ck_attendees_participant_id", "participant_id ~ '^HTN-[2-9A-HJKMNP-TV-Z]{4}-[2-9A-HJKMNP-TV-Z]{4}$'");
                    table.CheckConstraint("ck_attendees_shirt_size", "shirt_size IS NULL OR shirt_size IN ('XS', 'S', 'M', 'L', 'XL', '2XL')");
                    table.CheckConstraint("ck_attendees_sort_order", "sort_order BETWEEN 0 AND 49");
                    table.CheckConstraint("ck_attendees_ticket_version", "ticket_version >= 1");
                    table.ForeignKey(
                        name: "fk_attendees_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_attendees_registrations_registration_id",
                        column: x => x.registration_id,
                        principalTable: "registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_proofs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_proofs", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_proofs_registrations_registration_id",
                        column: x => x.registration_id,
                        principalTable: "registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_proofs_stored_files_file_id",
                        column: x => x.file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    payment_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    marked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    marked_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reverted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reverted_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revert_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.CheckConstraint("ck_payments_amount", "amount >= 0");
                    table.CheckConstraint("ck_payments_comp", "method <> 'Comp' OR (amount = 0 AND payment_account_id IS NULL)");
                    table.CheckConstraint("ck_payments_method", "method IN ('GCash', 'Bank', 'Cash', 'Comp')");
                    table.CheckConstraint("ck_payments_reverted", "(reverted_at IS NULL) = (revert_reason IS NULL) AND (reverted_at IS NULL OR reverted_at >= marked_at)");
                    table.ForeignKey(
                        name: "fk_payments_payment_accounts_payment_account_id",
                        column: x => x.payment_account_id,
                        principalTable: "payment_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_registrations_registration_id",
                        column: x => x.registration_id,
                        principalTable: "registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attendees_participant_id",
                table: "attendees",
                column: "participant_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendees_registration_id_sort_order",
                table: "attendees",
                columns: new[] { "registration_id", "sort_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_attendees_event_participant_id",
                table: "attendees",
                columns: new[] { "event_id", "participant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_proofs_file_id",
                table: "payment_proofs",
                column: "file_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_proofs_registration_id_uploaded_at",
                table: "payment_proofs",
                columns: new[] { "registration_id", "uploaded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_marked_at",
                table: "payments",
                column: "marked_at");

            migrationBuilder.CreateIndex(
                name: "ix_payments_payment_account_id",
                table: "payments",
                column: "payment_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_registration_id_marked_at",
                table: "payments",
                columns: new[] { "registration_id", "marked_at" });

            migrationBuilder.CreateIndex(
                name: "ux_payments_registration_open",
                table: "payments",
                column: "registration_id",
                unique: true,
                filter: "reverted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_registrations_event_id_created_at",
                table: "registrations",
                columns: new[] { "event_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_registrations_event_id_status_deadline",
                table: "registrations",
                columns: new[] { "event_id", "status", "deadline" });

            migrationBuilder.CreateIndex(
                name: "ix_registrations_ticket_type_id_status",
                table: "registrations",
                columns: new[] { "ticket_type_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_registrations_voucher_id",
                table: "registrations",
                column: "voucher_id",
                filter: "voucher_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_registrations_event_idempotency_key",
                table: "registrations",
                columns: new[] { "event_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_registrations_proof_token_hash",
                table: "registrations",
                column: "proof_token_hash",
                unique: true,
                filter: "proof_token_hash IS NOT NULL");

            // Search by email (admin list) and member matching by email (3C): case-insensitive lookups.
            // pg_trgm is not installed, so contains-searches stay sequential within one event's rows (event_id filter first).
            migrationBuilder.Sql("""
                CREATE INDEX ix_attendees_email_lower ON attendees (lower(email));
                """);

            // Registrations, attendees, payments and proofs are never deleted (cancel / revert with a reason instead).
            // Proofs are also immutable once uploaded (a new upload adds a row).
            migrationBuilder.Sql("""
                CREATE TRIGGER payment_proofs_append_only BEFORE UPDATE OR DELETE ON payment_proofs
                    FOR EACH ROW EXECUTE FUNCTION htn_reject_change();
                CREATE TRIGGER payment_proofs_no_truncate BEFORE TRUNCATE ON payment_proofs
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                REVOKE DELETE, TRUNCATE ON registrations, attendees, payments, payment_proofs FROM PUBLIC;
                REVOKE UPDATE ON payment_proofs FROM PUBLIC;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'htn_app') THEN
                        REVOKE DELETE, TRUNCATE ON registrations, attendees, payments, payment_proofs FROM htn_app;
                        REVOKE UPDATE ON payment_proofs FROM htn_app;
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // WARNING: rolling this back drops every registration, attendee, payment and proof record. That data is lost
            // (proof images stay on disk and in stored_files). Back up first.
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS payment_proofs_no_truncate ON payment_proofs;
                DROP TRIGGER IF EXISTS payment_proofs_append_only ON payment_proofs;
                DROP INDEX IF EXISTS ix_attendees_email_lower;
                """);

            migrationBuilder.DropTable(
                name: "attendees");

            migrationBuilder.DropTable(
                name: "payment_proofs");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "registrations");
        }
    }
}
