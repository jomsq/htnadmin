using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Phase 4 database review, guard pass.
    /// <list type="bullet">
    /// <item>Bounded labels (no empty elements, bounded total length), a reversal that can never predate its award,
    /// and a task counter that can never go negative.</item>
    /// <item><c>task_assignees_frozen</c>: who earns points on a Done or archived card cannot change, whatever the
    /// API does — defence in depth behind the endpoints, which take the task row lock and re-check the status.</item>
    /// <item><c>committees_lead_matches</c> / <c>committee_members_lead_matches</c>: the denormalized
    /// <c>committees.lead_person_id</c> (what the UI reads) and the active <c>role = 'Lead'</c> membership row (what
    /// authorization reads) can no longer drift apart. Both are deferred constraint triggers, so one transaction may
    /// move the lead across the two tables in either order and is only judged at commit.</item>
    /// </list>
    /// Functions are created with <c>CREATE OR REPLACE</c> and triggers are dropped first, so a half-rolled-back
    /// <c>Up</c> can be re-run.
    /// </summary>
    public partial class Tasks_Guards2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tasks_labels",
                table: "tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_points_awards_reversal",
                table: "points_awards");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tasks_labels",
                table: "tasks",
                sql: "array_position(labels, NULL) IS NULL AND array_position(labels, '') IS NULL AND cardinality(labels) <= 5 AND char_length(array_to_string(labels, ',')) <= 160");

            migrationBuilder.AddCheckConstraint(
                name: "ck_points_awards_reversal",
                table: "points_awards",
                sql: "(reversed_at IS NULL) = (reversal_reason IS NULL) AND (reversed_at IS NULL OR reversed_at >= awarded_at) AND (reversal_reason IS NULL OR char_length(reversal_reason) BETWEEN 1 AND 500)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_events_task_counter",
                table: "events",
                sql: "task_counter >= 0");

            migrationBuilder.Sql(
                @"
                -- Who earns points on a finished or archived card is frozen: reopen or restore it first.
                CREATE OR REPLACE FUNCTION htn_task_assignees_frozen() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    v_status text;
                    v_archived boolean;
                BEGIN
                    SELECT t.status, t.is_archived INTO v_status, v_archived
                    FROM tasks t WHERE t.id = COALESCE(NEW.task_id, OLD.task_id);
                    IF v_status = 'Done' OR v_archived THEN
                        RAISE EXCEPTION 'task_assignees: a done or archived task is frozen; reopen it first'
                            USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN COALESCE(NEW, OLD);
                END;
                $$;
                DROP TRIGGER IF EXISTS task_assignees_frozen ON task_assignees;
                CREATE TRIGGER task_assignees_frozen BEFORE INSERT OR UPDATE OR DELETE ON task_assignees
                    FOR EACH ROW EXECUTE FUNCTION htn_task_assignees_frozen();

                -- The lead column the UI reads and the Lead membership row authorization reads must agree. Deferred,
                -- so a hand-over may write the two tables in any order inside one transaction.
                CREATE OR REPLACE FUNCTION htn_committee_lead_matches() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    v_committee uuid;
                    v_lead uuid;
                    v_active boolean;
                    v_member uuid;
                BEGIN
                    -- One branch per table: plpgsql only resolves the NEW field of the branch it runs, and the two
                    -- tables have different columns.
                    IF TG_TABLE_NAME = 'committees' THEN
                        v_committee := NEW.id;
                    ELSE
                        v_committee := NEW.committee_id;
                    END IF;
                    SELECT c.lead_person_id, c.is_active INTO v_lead, v_active FROM committees c WHERE c.id = v_committee;
                    IF v_active IS NULL THEN
                        RETURN NULL; -- the committee is gone (it never is: committees are deactivated, not deleted)
                    END IF;
                    SELECT m.person_id INTO v_member FROM committee_members m
                    WHERE m.committee_id = v_committee AND m.left_at IS NULL AND m.role = 'Lead';
                    IF v_lead IS DISTINCT FROM v_member THEN
                        RAISE EXCEPTION 'committees: lead_person_id (%) and the active Lead membership (%) must agree',
                            v_lead, v_member USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NULL;
                END;
                $$;
                DROP TRIGGER IF EXISTS committees_lead_matches ON committees;
                CREATE CONSTRAINT TRIGGER committees_lead_matches AFTER INSERT OR UPDATE ON committees
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION htn_committee_lead_matches();
                DROP TRIGGER IF EXISTS committee_members_lead_matches ON committee_members;
                CREATE CONSTRAINT TRIGGER committee_members_lead_matches AFTER INSERT OR UPDATE ON committee_members
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION htn_committee_lead_matches();
                ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tasks_labels",
                table: "tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_points_awards_reversal",
                table: "points_awards");

            migrationBuilder.DropCheckConstraint(
                name: "ck_events_task_counter",
                table: "events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tasks_labels",
                table: "tasks",
                sql: "array_position(labels, NULL) IS NULL AND cardinality(labels) <= 5");

            migrationBuilder.AddCheckConstraint(
                name: "ck_points_awards_reversal",
                table: "points_awards",
                sql: "(reversed_at IS NULL) = (reversal_reason IS NULL) AND (reversal_reason IS NULL OR char_length(reversal_reason) BETWEEN 1 AND 500)");

            migrationBuilder.Sql(
                @"
                DROP TRIGGER IF EXISTS committee_members_lead_matches ON committee_members;
                DROP TRIGGER IF EXISTS committees_lead_matches ON committees;
                DROP TRIGGER IF EXISTS task_assignees_frozen ON task_assignees;
                DROP FUNCTION IF EXISTS htn_committee_lead_matches();
                DROP FUNCTION IF EXISTS htn_task_assignees_frozen();
                ");
        }
    }
}
