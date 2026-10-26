using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Committees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "committees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    lead_person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_committees", x => x.id);
                    table.UniqueConstraint("ak_committees_id_event_id", x => new { x.id, x.event_id });
                    table.CheckConstraint("ck_committees_deactivated", "is_active = (deactivated_at IS NULL)");
                    table.CheckConstraint("ck_committees_lead", "is_active OR lead_person_id IS NULL");
                    table.CheckConstraint("ck_committees_name", "char_length(name) BETWEEN 1 AND 60 AND name = btrim(name)");
                    table.CheckConstraint("ck_committees_sort_order", "sort_order BETWEEN 0 AND 1000");
                    table.ForeignKey(
                        name: "fk_committees_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_committees_people_lead_person_id",
                        column: x => x.lead_person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "committee_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    committee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    left_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_committee_members", x => x.id);
                    table.CheckConstraint("ck_committee_members_left", "left_at IS NULL OR left_at >= joined_at");
                    table.CheckConstraint("ck_committee_members_role", "role IN ('Lead', 'Member')");
                    table.ForeignKey(
                        name: "fk_committee_members_committees_committee_id_event_id",
                        columns: x => new { x.committee_id, x.event_id },
                        principalTable: "committees",
                        principalColumns: new[] { "id", "event_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_committee_members_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_volunteers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    mobile = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    committee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_volunteers", x => x.id);
                    table.CheckConstraint("ck_event_volunteers_committee", "is_active OR committee_id IS NULL");
                    table.CheckConstraint("ck_event_volunteers_deactivated", "is_active = (deactivated_at IS NULL)");
                    table.CheckConstraint("ck_event_volunteers_email", "email IS NULL OR (char_length(email) BETWEEN 3 AND 254 AND email = lower(email) AND email ~ '^[^@[:space:]]+@[^@[:space:]]+\\.[^@[:space:]]+$')");
                    table.CheckConstraint("ck_event_volunteers_mobile", "mobile IS NULL OR mobile ~ '^\\+639[0-9]{9}$'");
                    table.CheckConstraint("ck_event_volunteers_name", "char_length(name) BETWEEN 1 AND 120 AND name = btrim(name)");
                    table.ForeignKey(
                        name: "fk_event_volunteers_committees_committee_id_event_id",
                        columns: x => new { x.committee_id, x.event_id },
                        principalTable: "committees",
                        principalColumns: new[] { "id", "event_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_volunteers_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_committee_members_committee_id_event_id",
                table: "committee_members",
                columns: new[] { "committee_id", "event_id" });

            migrationBuilder.CreateIndex(
                name: "ix_committee_members_committee_id_joined_at",
                table: "committee_members",
                columns: new[] { "committee_id", "joined_at" });

            migrationBuilder.CreateIndex(
                name: "ix_committee_members_person_id",
                table: "committee_members",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ux_committee_members_active_lead",
                table: "committee_members",
                column: "committee_id",
                unique: true,
                filter: "left_at IS NULL AND role = 'Lead'");

            migrationBuilder.CreateIndex(
                name: "ux_committee_members_active_person",
                table: "committee_members",
                columns: new[] { "event_id", "person_id" },
                unique: true,
                filter: "left_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_committees_event_id_sort_order",
                table: "committees",
                columns: new[] { "event_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_committees_lead_person_id",
                table: "committees",
                column: "lead_person_id",
                filter: "lead_person_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_committees_event_name",
                table: "committees",
                columns: new[] { "event_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_volunteers_committee_id",
                table: "event_volunteers",
                column: "committee_id",
                filter: "committee_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_event_volunteers_committee_id_event_id",
                table: "event_volunteers",
                columns: new[] { "committee_id", "event_id" });

            migrationBuilder.CreateIndex(
                name: "ix_event_volunteers_event_id_name",
                table: "event_volunteers",
                columns: new[] { "event_id", "name" });

            // Committee names are unique per event case-insensitively ("Logistics" and "logistics" are the same team).
            // EF only models the plain (event_id, name) index, so it is replaced here by the lower() expression form
            // under the same name (the Events / Registrations_IntegrityGuards migrations do the same for voucher codes).
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS ux_committees_event_name;
                CREATE UNIQUE INDEX ux_committees_event_name ON committees (event_id, lower(name));
                """);

            // Database guards, defence in depth under the API's own rules. htn_reject_change() is from the Initial
            // migration. Nothing here is ever deleted: a committee and a volunteer are deactivated, and a member
            // leaving is a left_at timestamp, so the row stays as history.
            migrationBuilder.Sql("""
                CREATE FUNCTION htn_committees_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'committees are never deleted (deactivate with a reason)' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF (NEW.id, NEW.event_id, NEW.created_at) IS DISTINCT FROM (OLD.id, OLD.event_id, OLD.created_at) THEN
                        RAISE EXCEPTION 'committees: id, event and created_at are immutable' USING ERRCODE = 'check_violation';
                    END IF;
                    -- Deactivation is final: a committee is not brought back (create a new one).
                    IF NEW.is_active AND NOT OLD.is_active THEN
                        RAISE EXCEPTION 'committees: a deactivated committee cannot be reactivated' USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER committees_guard BEFORE UPDATE OR DELETE ON committees
                    FOR EACH ROW EXECUTE FUNCTION htn_committees_guard();
                CREATE TRIGGER committees_no_truncate BEFORE TRUNCATE ON committees
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                -- Membership history: append-only apart from the role and the single left_at stamp.
                CREATE FUNCTION htn_committee_members_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'committee memberships are never deleted (they record left_at)' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF (NEW.id, NEW.committee_id, NEW.event_id, NEW.person_id, NEW.joined_at)
                       IS DISTINCT FROM (OLD.id, OLD.committee_id, OLD.event_id, OLD.person_id, OLD.joined_at) THEN
                        RAISE EXCEPTION 'committee_members: committee, person and joined_at are immutable' USING ERRCODE = 'check_violation';
                    END IF;
                    IF OLD.left_at IS NOT NULL
                       AND (NEW.left_at, NEW.role) IS DISTINCT FROM (OLD.left_at, OLD.role) THEN
                        RAISE EXCEPTION 'committee_members: a membership that has ended cannot be changed' USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER committee_members_guard BEFORE UPDATE OR DELETE ON committee_members
                    FOR EACH ROW EXECUTE FUNCTION htn_committee_members_guard();
                CREATE TRIGGER committee_members_no_truncate BEFORE TRUNCATE ON committee_members
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                CREATE FUNCTION htn_event_volunteers_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'volunteers are never deleted (deactivate with a reason)' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF (NEW.id, NEW.event_id, NEW.created_at) IS DISTINCT FROM (OLD.id, OLD.event_id, OLD.created_at) THEN
                        RAISE EXCEPTION 'event_volunteers: id, event and created_at are immutable' USING ERRCODE = 'check_violation';
                    END IF;
                    IF NEW.is_active AND NOT OLD.is_active THEN
                        RAISE EXCEPTION 'event_volunteers: a deactivated volunteer cannot be reactivated' USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER event_volunteers_guard BEFORE UPDATE OR DELETE ON event_volunteers
                    FOR EACH ROW EXECUTE FUNCTION htn_event_volunteers_guard();
                CREATE TRIGGER event_volunteers_no_truncate BEFORE TRUNCATE ON event_volunteers
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                REVOKE DELETE, TRUNCATE ON committees, committee_members, event_volunteers FROM PUBLIC;
                -- Production runs the API as a non-owner role named htn_app (migrations run as the owner).
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'htn_app') THEN
                        REVOKE DELETE, TRUNCATE ON committees, committee_members, event_volunteers FROM htn_app;
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // WARNING: rolling this back drops every committee, membership history and volunteer of every event.
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS event_volunteers_no_truncate ON event_volunteers;
                DROP TRIGGER IF EXISTS event_volunteers_guard ON event_volunteers;
                DROP TRIGGER IF EXISTS committee_members_no_truncate ON committee_members;
                DROP TRIGGER IF EXISTS committee_members_guard ON committee_members;
                DROP TRIGGER IF EXISTS committees_no_truncate ON committees;
                DROP TRIGGER IF EXISTS committees_guard ON committees;
                DROP FUNCTION IF EXISTS htn_event_volunteers_guard();
                DROP FUNCTION IF EXISTS htn_committee_members_guard();
                DROP FUNCTION IF EXISTS htn_committees_guard();
                """);

            migrationBuilder.DropTable(
                name: "committee_members");

            migrationBuilder.DropTable(
                name: "event_volunteers");

            migrationBuilder.DropTable(
                name: "committees");
        }
    }
}
