using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Organization_Settings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_person_history_kind",
                table: "person_history");

            migrationBuilder.CreateTable(
                name: "payment_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    bank_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    account_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    account_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deactivation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_accounts", x => x.id);
                    table.CheckConstraint("ck_payment_accounts_bank_name", "kind <> 'Bank' OR bank_name IS NOT NULL");
                    table.CheckConstraint("ck_payment_accounts_deactivated", "(is_active AND deactivated_at IS NULL AND deactivation_reason IS NULL) OR (NOT is_active AND deactivated_at IS NOT NULL AND deactivation_reason IS NOT NULL)");
                    table.CheckConstraint("ck_payment_accounts_default_active", "NOT is_default OR is_active");
                    table.CheckConstraint("ck_payment_accounts_kind", "kind IN ('GCash', 'Bank', 'Cash')");
                    table.CheckConstraint("ck_payment_accounts_number", "kind = 'Cash' OR account_number IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "positions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    holder_id = table.Column<Guid>(type: "uuid", nullable: true),
                    holder_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_positions", x => x.id);
                    table.CheckConstraint("ck_positions_held_only_if_active", "holder_id IS NULL OR is_active");
                    table.CheckConstraint("ck_positions_holder_since", "(holder_id IS NULL) = (holder_since IS NULL)");
                    table.CheckConstraint("ck_positions_kind", "kind IN ('BoardSeat', 'GoonPosition')");
                    table.ForeignKey(
                        name: "fk_positions_people_holder_id",
                        column: x => x.holder_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stored_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    purpose = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    original_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stored_files", x => x.id);
                    table.CheckConstraint("ck_stored_files_sha256", "sha256 ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_stored_files_size", "size_bytes > 0 AND size_bytes <= 1048576");
                });

            migrationBuilder.CreateTable(
                name: "position_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    position_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    assigned_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    end_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_position_assignments", x => x.id);
                    table.CheckConstraint("ck_position_assignments_end_reason", "ended_at IS NOT NULL OR (ended_by_id IS NULL AND end_reason IS NULL)");
                    table.CheckConstraint("ck_position_assignments_period", "ended_at IS NULL OR ended_at >= started_at");
                    table.ForeignKey(
                        name: "fk_position_assignments_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_position_assignments_positions_position_id",
                        column: x => x.position_id,
                        principalTable: "positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "org_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    lead_multiplier = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: false),
                    collab_multiplier = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: false),
                    assistant_multiplier = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: false),
                    preset_small = table.Column<int>(type: "integer", nullable: false),
                    preset_medium = table.Column<int>(type: "integer", nullable: false),
                    preset_large = table.Column<int>(type: "integer", nullable: false),
                    preset_xl = table.Column<int>(type: "integer", nullable: false),
                    require_task_approval = table.Column<bool>(type: "boolean", nullable: false),
                    brand_color = table.Column<string>(type: "character(7)", fixedLength: true, maxLength: 7, nullable: false),
                    logo_light_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    logo_dark_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_deadline_hours = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_org_settings", x => x.id);
                    table.CheckConstraint("ck_org_settings_brand_color", "brand_color ~ '^#[0-9A-Fa-f]{6}$'");
                    table.CheckConstraint("ck_org_settings_multipliers", "lead_multiplier BETWEEN 0 AND 2 AND collab_multiplier BETWEEN 0 AND 2 AND assistant_multiplier BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_org_settings_payment_deadline", "payment_deadline_hours BETWEEN 1 AND 336");
                    table.CheckConstraint("ck_org_settings_presets", "preset_small >= 1 AND preset_small < preset_medium AND preset_medium < preset_large AND preset_large < preset_xl AND preset_xl <= 1000");
                    table.CheckConstraint("ck_org_settings_singleton", "id = 1");
                    table.ForeignKey(
                        name: "fk_org_settings_stored_files_logo_dark_file_id",
                        column: x => x.logo_dark_file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_org_settings_stored_files_logo_light_file_id",
                        column: x => x.logo_light_file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Widen the history kinds. NOT VALID + VALIDATE: the ADD takes only a brief lock and the scan of existing
            // rows runs under a weaker lock that doesn't block inserts.
            migrationBuilder.Sql("""
                ALTER TABLE person_history ADD CONSTRAINT ck_person_history_kind
                    CHECK (kind IN ('created', 'level_changed', 'position_changed', 'deactivated', 'position_assigned', 'position_vacated')) NOT VALID;
                ALTER TABLE person_history VALIDATE CONSTRAINT ck_person_history_kind;
                """);

            // Seed data for every environment: the settings row (values = OrgSettings.Defaults()) and the seven Board of
            // Trustees seats. Raw SQL rather than HasData so later model changes never generate update/delete seed ops.
            migrationBuilder.Sql("""
                INSERT INTO org_settings (id, lead_multiplier, collab_multiplier, assistant_multiplier,
                                          preset_small, preset_medium, preset_large, preset_xl,
                                          require_task_approval, brand_color, payment_deadline_hours)
                VALUES (1, 1.0, 0.7, 0.3, 10, 20, 40, 80, TRUE, '#B71C1C', 48)
                ON CONFLICT (id) DO NOTHING;

                INSERT INTO positions (id, kind, title, sort_order, is_active, created_at) VALUES
                    ('0192f0a0-0000-7000-8000-0000000b0001', 'BoardSeat', 'Chairperson',         1, TRUE, '2026-01-01T00:00:00Z'),
                    ('0192f0a0-0000-7000-8000-0000000b0002', 'BoardSeat', 'Vice Chairperson',    2, TRUE, '2026-01-01T00:00:00Z'),
                    ('0192f0a0-0000-7000-8000-0000000b0003', 'BoardSeat', 'Treasurer',           3, TRUE, '2026-01-01T00:00:00Z'),
                    ('0192f0a0-0000-7000-8000-0000000b0004', 'BoardSeat', 'Corporate Secretary', 4, TRUE, '2026-01-01T00:00:00Z'),
                    ('0192f0a0-0000-7000-8000-0000000b0005', 'BoardSeat', 'Trustee',             5, TRUE, '2026-01-01T00:00:00Z'),
                    ('0192f0a0-0000-7000-8000-0000000b0006', 'BoardSeat', 'Trustee',             6, TRUE, '2026-01-01T00:00:00Z'),
                    ('0192f0a0-0000-7000-8000-0000000b0007', 'BoardSeat', 'Trustee',             7, TRUE, '2026-01-01T00:00:00Z')
                ON CONFLICT (id) DO NOTHING;
                """);

            // Position terms are history: a row may only be updated once, to close an open term (ended_at set, the
            // who/where/when columns unchanged). DELETE and TRUNCATE are rejected for every role, like audit_entries.
            // None of the new tables are ever deleted from (void/deactivate instead), so DELETE/TRUNCATE are revoked too.
            migrationBuilder.Sql("""
                CREATE FUNCTION htn_position_assignment_close_only() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'UPDATE'
                       AND OLD.ended_at IS NULL AND NEW.ended_at IS NOT NULL
                       AND NEW.id = OLD.id
                       AND NEW.position_id = OLD.position_id
                       AND NEW.person_id = OLD.person_id
                       AND NEW.started_at = OLD.started_at
                       AND NEW.assigned_by_id IS NOT DISTINCT FROM OLD.assigned_by_id THEN
                        RETURN NEW;
                    END IF;
                    RAISE EXCEPTION 'position_assignments only allows closing an open term once (% rejected)', TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;
                CREATE TRIGGER position_assignments_close_only BEFORE UPDATE OR DELETE ON position_assignments
                    FOR EACH ROW EXECUTE FUNCTION htn_position_assignment_close_only();
                CREATE TRIGGER position_assignments_no_truncate BEFORE TRUNCATE ON position_assignments
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                REVOKE DELETE, TRUNCATE ON positions, position_assignments, org_settings, payment_accounts, stored_files FROM PUBLIC;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'htn_app') THEN
                        REVOKE DELETE, TRUNCATE ON positions, position_assignments, org_settings, payment_accounts, stored_files FROM htn_app;
                    END IF;
                END
                $$;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_org_settings_logo_dark_file_id",
                table: "org_settings",
                column: "logo_dark_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_org_settings_logo_light_file_id",
                table: "org_settings",
                column: "logo_light_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_accounts_is_active",
                table: "payment_accounts",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ux_payment_accounts_single_default",
                table: "payment_accounts",
                column: "is_default",
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ix_position_assignments_person_id_started_at",
                table: "position_assignments",
                columns: new[] { "person_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_position_assignments_position_id_started_at",
                table: "position_assignments",
                columns: new[] { "position_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ux_position_assignments_current",
                table: "position_assignments",
                column: "position_id",
                unique: true,
                filter: "ended_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_positions_holder_id",
                table: "positions",
                column: "holder_id");

            migrationBuilder.CreateIndex(
                name: "ix_positions_kind_sort_order",
                table: "positions",
                columns: new[] { "kind", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ux_positions_one_board_seat_per_person",
                table: "positions",
                column: "holder_id",
                unique: true,
                filter: "kind = 'BoardSeat' AND holder_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stored_files_purpose",
                table: "stored_files",
                column: "purpose");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // WARNING: rolling this back drops positions, position terms, org settings, payment accounts and stored-file
            // metadata. That data is lost (uploaded files stay on disk). Back up first.
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS position_assignments_no_truncate ON position_assignments;
                DROP TRIGGER IF EXISTS position_assignments_close_only ON position_assignments;
                DROP FUNCTION IF EXISTS htn_position_assignment_close_only();
                """);

            migrationBuilder.DropTable(
                name: "org_settings");

            migrationBuilder.DropTable(
                name: "payment_accounts");

            migrationBuilder.DropTable(
                name: "position_assignments");

            migrationBuilder.DropTable(
                name: "stored_files");

            migrationBuilder.DropTable(
                name: "positions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_person_history_kind",
                table: "person_history");

            // person_history is append-only, so rows of the new kinds (position_assigned/vacated) stay. NOT VALID keeps the
            // narrow constraint from failing on them while still checking every new row.
            migrationBuilder.Sql("""
                ALTER TABLE person_history ADD CONSTRAINT ck_person_history_kind
                    CHECK (kind IN ('created', 'level_changed', 'position_changed', 'deactivated')) NOT VALID;
                """);
        }
    }
}
