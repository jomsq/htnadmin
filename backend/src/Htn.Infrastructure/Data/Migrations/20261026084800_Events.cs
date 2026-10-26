using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Events : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_stored_files_size",
                table: "stored_files");

            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    venue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    cover_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    manual_registration = table.Column<bool>(type: "boolean", nullable: false),
                    group_registration = table.Column<bool>(type: "boolean", nullable: false),
                    public_page = table.Column<bool>(type: "boolean", nullable: false),
                    proof_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_main_event = table.Column<bool>(type: "boolean", nullable: false),
                    membership_fee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    membership_year = table.Column<int>(type: "integer", nullable: true),
                    email_accent_color = table.Column<string>(type: "character(7)", fixedLength: true, maxLength: 7, nullable: true),
                    email_payment_account_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false, defaultValueSql: "'{}'::uuid[]"),
                    payment_deadline_hours = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_events", x => x.id);
                    table.CheckConstraint("ck_events_capacity", "capacity BETWEEN 1 AND 100000");
                    table.CheckConstraint("ck_events_email_accent_color", "email_accent_color IS NULL OR email_accent_color ~ '^#[0-9A-F]{6}$'");
                    table.CheckConstraint("ck_events_membership", "(is_main_event AND membership_fee BETWEEN 0 AND 100000 AND membership_year BETWEEN 2000 AND 2100) OR (NOT is_main_event AND membership_fee IS NULL AND membership_year IS NULL)");
                    table.CheckConstraint("ck_events_payment_deadline", "payment_deadline_hours IS NULL OR payment_deadline_hours BETWEEN 1 AND 336");
                    table.CheckConstraint("ck_events_period", "ends_at > starts_at");
                    table.CheckConstraint("ck_events_proof_mode", "proof_mode IN ('Upload', 'ReplyEmail')");
                    table.CheckConstraint("ck_events_slug", "slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND char_length(slug) BETWEEN 3 AND 60");
                    table.CheckConstraint("ck_events_status", "status IN ('Draft', 'OnSale', 'Closed')");
                    table.ForeignKey(
                        name: "fk_events_stored_files_cover_file_id",
                        column: x => x.cover_file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ticket_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    group_size = table.Column<int>(type: "integer", nullable: false),
                    sales_starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sales_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    visibility = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_types", x => x.id);
                    table.CheckConstraint("ck_ticket_types_deactivated", "is_active = (deactivated_at IS NULL)");
                    table.CheckConstraint("ck_ticket_types_group_size", "group_size BETWEEN 1 AND 50");
                    table.CheckConstraint("ck_ticket_types_price", "price BETWEEN 0 AND 1000000");
                    table.CheckConstraint("ck_ticket_types_quantity", "quantity BETWEEN 1 AND 100000");
                    table.CheckConstraint("ck_ticket_types_sales_window", "sales_starts_at IS NULL OR sales_ends_at IS NULL OR sales_ends_at > sales_starts_at");
                    table.CheckConstraint("ck_ticket_types_visibility", "visibility IN ('Public', 'Hidden')");
                    table.ForeignKey(
                        name: "fk_ticket_types_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vouchers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    percent_off = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    amount_off = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    ticket_type_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false, defaultValueSql: "'{}'::uuid[]"),
                    usage_limit = table.Column<int>(type: "integer", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vouchers", x => x.id);
                    table.CheckConstraint("ck_vouchers_code", "code ~ '^[A-Z0-9-]{3,30}$'");
                    table.CheckConstraint("ck_vouchers_deactivated", "is_active = (deactivated_at IS NULL)");
                    table.CheckConstraint("ck_vouchers_kind", "kind IN ('Percent', 'Fixed')");
                    table.CheckConstraint("ck_vouchers_source", "source IN ('Manual', 'SpeakerPlusOne')");
                    table.CheckConstraint("ck_vouchers_usage_limit", "usage_limit IS NULL OR usage_limit BETWEEN 1 AND 100000");
                    table.CheckConstraint("ck_vouchers_value", "(kind = 'Percent' AND percent_off BETWEEN 1 AND 100 AND amount_off IS NULL) OR (kind = 'Fixed' AND amount_off > 0 AND amount_off <= 1000000 AND percent_off IS NULL)");
                    table.ForeignKey(
                        name: "fk_vouchers_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_stored_files_size",
                table: "stored_files",
                sql: "size_bytes > 0 AND size_bytes <= 5242880");

            migrationBuilder.CreateIndex(
                name: "ix_events_cover_file_id",
                table: "events",
                column: "cover_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_events_ends_at",
                table: "events",
                column: "ends_at");

            migrationBuilder.CreateIndex(
                name: "ix_events_slug",
                table: "events",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_events_status_starts_at",
                table: "events",
                columns: new[] { "status", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ux_events_main_event_year",
                table: "events",
                column: "membership_year",
                unique: true,
                filter: "is_main_event");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_types_event_id_sort_order",
                table: "ticket_types",
                columns: new[] { "event_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_vouchers_event_id",
                table: "vouchers",
                column: "event_id");

            // Voucher codes are unique per event, case-insensitively. Codes are stored upper-case (ck_vouchers_code), and
            // the expression index keeps that true even for rows written outside the app. Not expressible in the EF model.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_vouchers_event_code ON vouchers (event_id, upper(code));
                """);

            // Events, ticket types and vouchers are never deleted (close / deactivate instead).
            migrationBuilder.Sql("""
                REVOKE DELETE, TRUNCATE ON events, ticket_types, vouchers FROM PUBLIC;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'htn_app') THEN
                        REVOKE DELETE, TRUNCATE ON events, ticket_types, vouchers FROM htn_app;
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // WARNING: rolling this back drops events, ticket types and vouchers. That data is lost (cover files stay on
            // disk and in stored_files). Back up first.
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_vouchers_event_code;");

            migrationBuilder.DropTable(
                name: "ticket_types");

            migrationBuilder.DropTable(
                name: "vouchers");

            migrationBuilder.DropTable(
                name: "events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stored_files_size",
                table: "stored_files");

            // stored_files is never deleted from, so covers over 1 MB stay. NOT VALID keeps the narrow constraint from
            // failing on them while still checking every new row.
            migrationBuilder.Sql("""
                ALTER TABLE stored_files ADD CONSTRAINT ck_stored_files_size
                    CHECK (size_bytes > 0 AND size_bytes <= 1048576) NOT VALID;
                """);
        }
    }
}
