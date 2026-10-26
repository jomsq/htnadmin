using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CheckIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "check_in_scans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attendee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    station = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    staff_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_check_in_scans", x => x.id);
                    table.CheckConstraint("ck_check_in_scans_attendee", "attendee_id IS NOT NULL OR result = 'Invalid'");
                    table.CheckConstraint("ck_check_in_scans_method", "method IN ('Qr', 'Manual')");
                    table.CheckConstraint("ck_check_in_scans_result", "result IN ('Welcome', 'AlreadyCheckedIn', 'NotPaid', 'Cancelled', 'Invalid')");
                    table.CheckConstraint("ck_check_in_scans_station", "char_length(station) BETWEEN 1 AND 40 AND station = btrim(station)");
                    table.ForeignKey(
                        name: "fk_check_in_scans_attendees_attendee_id",
                        column: x => x.attendee_id,
                        principalTable: "attendees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_check_in_scans_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_check_in_scans_users_staff_user_id",
                        column: x => x.staff_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_registrations_checked_in",
                table: "registrations",
                sql: "status <> 'CheckedIn' OR checked_in_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_attendees_event_id_checked_in_station",
                table: "attendees",
                columns: new[] { "event_id", "checked_in_station" },
                filter: "checked_in_at IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_attendees_checked_in",
                table: "attendees",
                sql: "(checked_in_at IS NULL) = (checked_in_station IS NULL) AND (checked_in_at IS NULL) = (checked_in_by_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_check_in_scans_attendee_id",
                table: "check_in_scans",
                column: "attendee_id",
                filter: "attendee_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_check_in_scans_event_id_at",
                table: "check_in_scans",
                columns: new[] { "event_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_check_in_scans_event_id_station_at",
                table: "check_in_scans",
                columns: new[] { "event_id", "station", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_check_in_scans_staff_user_id",
                table: "check_in_scans",
                column: "staff_user_id");

            // Member rule (3C): "has an attendee with this email (case-insensitive) checked in at any event".
            migrationBuilder.Sql("""
                CREATE INDEX ix_attendees_checked_in_email_lower ON attendees (lower(email)) WHERE checked_in_at IS NOT NULL;
                """);

            // The scan log is append-only like audit_entries / person_history: UPDATE, DELETE and TRUNCATE are rejected
            // by triggers for every role (owner included), and the privileges are revoked from PUBLIC and htn_app.
            migrationBuilder.Sql("""
                CREATE TRIGGER check_in_scans_append_only BEFORE UPDATE OR DELETE ON check_in_scans
                    FOR EACH ROW EXECUTE FUNCTION htn_reject_change();
                CREATE TRIGGER check_in_scans_no_truncate BEFORE TRUNCATE ON check_in_scans
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                REVOKE UPDATE, DELETE, TRUNCATE ON check_in_scans FROM PUBLIC;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'htn_app') THEN
                        REVOKE UPDATE, DELETE, TRUNCATE ON check_in_scans FROM htn_app;
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // WARNING: rolling this back drops the whole check-in scan log. Attendee and registration check-in fields stay.
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS check_in_scans_no_truncate ON check_in_scans;
                DROP TRIGGER IF EXISTS check_in_scans_append_only ON check_in_scans;
                DROP INDEX IF EXISTS ix_attendees_checked_in_email_lower;
                """);

            migrationBuilder.DropTable(
                name: "check_in_scans");

            migrationBuilder.DropCheckConstraint(
                name: "ck_registrations_checked_in",
                table: "registrations");

            migrationBuilder.DropIndex(
                name: "ix_attendees_event_id_checked_in_station",
                table: "attendees");

            migrationBuilder.DropCheckConstraint(
                name: "ck_attendees_checked_in",
                table: "attendees");
        }
    }
}
