using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

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
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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

            migrationBuilder.InsertData(
                table: "org_settings",
                columns: new[] { "id", "assistant_multiplier", "brand_color", "collab_multiplier", "lead_multiplier", "logo_dark_file_id", "logo_light_file_id", "payment_deadline_hours", "preset_large", "preset_medium", "preset_small", "preset_xl", "require_task_approval", "updated_at", "updated_by_id" },
                values: new object[] { 1, 0.3m, "#B71C1C", 0.7m, 1.0m, null, null, 48, 40, 20, 10, 80, true, null, null });

            migrationBuilder.InsertData(
                table: "positions",
                columns: new[] { "id", "created_at", "holder_id", "holder_since", "is_active", "kind", "sort_order", "title" },
                values: new object[,]
                {
                    { new Guid("0192f0a0-0000-7000-8000-0000000b0001"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "BoardSeat", 1, "Chairperson" },
                    { new Guid("0192f0a0-0000-7000-8000-0000000b0002"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "BoardSeat", 2, "Vice Chairperson" },
                    { new Guid("0192f0a0-0000-7000-8000-0000000b0003"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "BoardSeat", 3, "Treasurer" },
                    { new Guid("0192f0a0-0000-7000-8000-0000000b0004"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "BoardSeat", 4, "Corporate Secretary" },
                    { new Guid("0192f0a0-0000-7000-8000-0000000b0005"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "BoardSeat", 5, "Trustee" },
                    { new Guid("0192f0a0-0000-7000-8000-0000000b0006"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "BoardSeat", 6, "Trustee" },
                    { new Guid("0192f0a0-0000-7000-8000-0000000b0007"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "BoardSeat", 7, "Trustee" }
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_person_history_kind",
                table: "person_history",
                sql: "kind IN ('created', 'level_changed', 'position_changed', 'deactivated', 'position_assigned', 'position_vacated')");

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

            migrationBuilder.AddCheckConstraint(
                name: "ck_person_history_kind",
                table: "person_history",
                sql: "kind IN ('created', 'level_changed', 'position_changed', 'deactivated')");
        }
    }
}
