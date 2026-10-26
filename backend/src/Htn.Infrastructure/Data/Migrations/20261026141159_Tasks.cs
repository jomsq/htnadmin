using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Tasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "task_counter",
                table: "events",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    committee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    reference = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    base_points = table.Column<int>(type: "integer", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    labels = table.Column<List<string>>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archived_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    archive_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tasks", x => x.id);
                    table.CheckConstraint("ck_tasks_approved", "(status = 'Done') = (approved_at IS NOT NULL)");
                    table.CheckConstraint("ck_tasks_archived", "is_archived = (archived_at IS NOT NULL) AND (is_archived OR archive_reason IS NULL) AND (archive_reason IS NULL OR char_length(archive_reason) BETWEEN 1 AND 500)");
                    table.CheckConstraint("ck_tasks_base_points", "base_points BETWEEN 0 AND 1000");
                    table.CheckConstraint("ck_tasks_description", "description IS NULL OR char_length(description) BETWEEN 1 AND 4000");
                    table.CheckConstraint("ck_tasks_labels", "array_position(labels, NULL) IS NULL AND cardinality(labels) <= 5");
                    table.CheckConstraint("ck_tasks_number", "number > 0");
                    table.CheckConstraint("ck_tasks_reference", "reference = 'HTN-' || number::text");
                    table.CheckConstraint("ck_tasks_sort_order", "sort_order BETWEEN 0 AND 1000000");
                    table.CheckConstraint("ck_tasks_status", "status IN ('Backlog', 'ToDo', 'InProgress', 'Review', 'Done')");
                    table.CheckConstraint("ck_tasks_title", "char_length(title) BETWEEN 1 AND 140 AND title = btrim(title)");
                    table.ForeignKey(
                        name: "fk_tasks_committees_committee_id_event_id",
                        columns: x => new { x.committee_id, x.event_id },
                        principalTable: "committees",
                        principalColumns: new[] { "id", "event_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tasks_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "points_awards",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    committee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    base_points = table.Column<int>(type: "integer", nullable: false),
                    multiplier = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
                    points = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    awarded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    awarded_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reversed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_points_awards", x => x.id);
                    table.CheckConstraint("ck_points_awards_base_points", "base_points BETWEEN 0 AND 1000");
                    table.CheckConstraint("ck_points_awards_multiplier", "multiplier BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_points_awards_points", "points = round(base_points * multiplier, 2)");
                    table.CheckConstraint("ck_points_awards_reversal", "(reversed_at IS NULL) = (reversal_reason IS NULL) AND (reversal_reason IS NULL OR char_length(reversal_reason) BETWEEN 1 AND 500)");
                    table.CheckConstraint("ck_points_awards_role", "role IN ('Lead', 'Collab', 'Assistant')");
                    table.ForeignKey(
                        name: "fk_points_awards_committees_committee_id",
                        column: x => x.committee_id,
                        principalTable: "committees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_points_awards_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_points_awards_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_points_awards_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_activities", x => x.id);
                    table.CheckConstraint("ck_task_activities_kind", "kind IN ('created', 'edited', 'moved', 'points_changed', 'assignee_added', 'assignee_removed', 'checklist_added', 'checklist_toggled', 'checklist_removed', 'attachment_added', 'attachment_removed', 'comment_added', 'comment_edited', 'comment_deleted', 'approved', 'reopened', 'archived', 'restored')");
                    table.CheckConstraint("ck_task_activities_text", "char_length(text) BETWEEN 1 AND 300");
                    table.ForeignKey(
                        name: "fk_task_activities_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_assignees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    volunteer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    role = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    added_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_assignees", x => x.id);
                    table.CheckConstraint("ck_task_assignees_role", "role IN ('Lead', 'Collab', 'Assistant')");
                    table.CheckConstraint("ck_task_assignees_volunteer_role", "volunteer_id IS NULL OR role = 'Assistant'");
                    table.CheckConstraint("ck_task_assignees_who", "(person_id IS NULL) <> (volunteer_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_task_assignees_event_volunteers_volunteer_id",
                        column: x => x.volunteer_id,
                        principalTable: "event_volunteers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_assignees_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_assignees_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    uploaded_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    removed_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_attachments", x => x.id);
                    table.CheckConstraint("ck_task_attachments_file_name", "char_length(file_name) BETWEEN 1 AND 200");
                    table.ForeignKey(
                        name: "fk_task_attachments_stored_files_file_id",
                        column: x => x.file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_attachments_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_checklist_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_done = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    done_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    done_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_checklist_items", x => x.id);
                    table.CheckConstraint("ck_task_checklist_items_done", "is_done = (done_at IS NOT NULL)");
                    table.CheckConstraint("ck_task_checklist_items_sort_order", "sort_order BETWEEN 0 AND 1000000");
                    table.CheckConstraint("ck_task_checklist_items_text", "char_length(text) BETWEEN 1 AND 200 AND text = btrim(text)");
                    table.ForeignKey(
                        name: "fk_task_checklist_items_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    mentioned_person_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false, defaultValueSql: "'{}'::uuid[]"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    delete_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_comments", x => x.id);
                    table.CheckConstraint("ck_task_comments_body", "char_length(body) BETWEEN 1 AND 4000");
                    table.CheckConstraint("ck_task_comments_deleted", "(deleted_at IS NULL) = (delete_reason IS NULL) AND (delete_reason IS NULL OR char_length(delete_reason) BETWEEN 1 AND 500)");
                    table.CheckConstraint("ck_task_comments_mentions", "array_position(mentioned_person_ids, NULL) IS NULL AND cardinality(mentioned_person_ids) <= 10");
                    table.ForeignKey(
                        name: "fk_task_comments_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_points_awards_committee_id_person_id",
                table: "points_awards",
                columns: new[] { "committee_id", "person_id" },
                filter: "reversed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_points_awards_event_id_person_id",
                table: "points_awards",
                columns: new[] { "event_id", "person_id" },
                filter: "reversed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_points_awards_person_id_awarded_at",
                table: "points_awards",
                columns: new[] { "person_id", "awarded_at" },
                filter: "reversed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_points_awards_task_id",
                table: "points_awards",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ux_points_awards_live_task_person",
                table: "points_awards",
                columns: new[] { "task_id", "person_id" },
                unique: true,
                filter: "reversed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_task_activities_task_id_at",
                table: "task_activities",
                columns: new[] { "task_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_task_assignees_person_id",
                table: "task_assignees",
                column: "person_id",
                filter: "person_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_task_assignees_task_id",
                table: "task_assignees",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_assignees_volunteer_id",
                table: "task_assignees",
                column: "volunteer_id");

            migrationBuilder.CreateIndex(
                name: "ux_task_assignees_person",
                table: "task_assignees",
                columns: new[] { "task_id", "person_id" },
                unique: true,
                filter: "person_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_task_assignees_volunteer",
                table: "task_assignees",
                columns: new[] { "task_id", "volunteer_id" },
                unique: true,
                filter: "volunteer_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_task_attachments_file_id",
                table: "task_attachments",
                column: "file_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_attachments_task_id_uploaded_at",
                table: "task_attachments",
                columns: new[] { "task_id", "uploaded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_task_checklist_items_task_id_sort_order",
                table: "task_checklist_items",
                columns: new[] { "task_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_task_comments_task_id_created_at",
                table: "task_comments",
                columns: new[] { "task_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_committee_id_event_id",
                table: "tasks",
                columns: new[] { "committee_id", "event_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_committee_id_status",
                table: "tasks",
                columns: new[] { "committee_id", "status" },
                filter: "NOT is_archived");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_event_id_status_sort_order",
                table: "tasks",
                columns: new[] { "event_id", "status", "sort_order" },
                filter: "NOT is_archived");

            migrationBuilder.CreateIndex(
                name: "ux_tasks_event_number",
                table: "tasks",
                columns: new[] { "event_id", "number" },
                unique: true);

            // Database guards, defence in depth under the API's own rules (htn_reject_change() is from the Initial
            // migration). Nothing here is ever deleted: a task is archived with a reason, a comment and an attachment
            // are soft-removed, and an award is reversed.
            migrationBuilder.Sql("""
                -- The activity log is append-only, exactly like person_history and audit_entries.
                CREATE TRIGGER task_activities_append_only BEFORE UPDATE OR DELETE ON task_activities
                    FOR EACH ROW EXECUTE FUNCTION htn_reject_change();
                CREATE TRIGGER task_activities_no_truncate BEFORE TRUNCATE ON task_activities
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                -- Points are append-only like finance: only the three reversal columns may ever change, once.
                CREATE FUNCTION htn_points_awards_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'points awards are reversed with a reason, never deleted' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF (NEW.id, NEW.task_id, NEW.event_id, NEW.committee_id, NEW.person_id, NEW.role, NEW.base_points,
                        NEW.multiplier, NEW.points, NEW.awarded_at, NEW.awarded_by_id)
                       IS DISTINCT FROM
                       (OLD.id, OLD.task_id, OLD.event_id, OLD.committee_id, OLD.person_id, OLD.role, OLD.base_points,
                        OLD.multiplier, OLD.points, OLD.awarded_at, OLD.awarded_by_id) THEN
                        RAISE EXCEPTION 'points_awards: an award is a snapshot and cannot be rewritten' USING ERRCODE = 'check_violation';
                    END IF;
                    IF OLD.reversed_at IS NOT NULL THEN
                        RAISE EXCEPTION 'points_awards: a reversed award cannot change again' USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER points_awards_guard BEFORE UPDATE OR DELETE ON points_awards
                    FOR EACH ROW EXECUTE FUNCTION htn_points_awards_guard();
                CREATE TRIGGER points_awards_no_truncate BEFORE TRUNCATE ON points_awards
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                -- A task keeps its identity, its event and its reference number for ever.
                CREATE FUNCTION htn_tasks_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'tasks are archived with a reason, never deleted' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF (NEW.id, NEW.event_id, NEW.number, NEW.reference, NEW.created_at)
                       IS DISTINCT FROM (OLD.id, OLD.event_id, OLD.number, OLD.reference, OLD.created_at) THEN
                        RAISE EXCEPTION 'tasks: id, event, number, reference and created_at are immutable' USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER tasks_guard BEFORE UPDATE OR DELETE ON tasks
                    FOR EACH ROW EXECUTE FUNCTION htn_tasks_guard();
                CREATE TRIGGER tasks_no_truncate BEFORE TRUNCATE ON tasks
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                -- Comments: the author, the task and the body of a deleted comment stand; deleting is a stamp + reason.
                CREATE FUNCTION htn_task_comments_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'comments are deleted with a reason (soft), never removed' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF (NEW.id, NEW.task_id, NEW.author_id, NEW.created_at)
                       IS DISTINCT FROM (OLD.id, OLD.task_id, OLD.author_id, OLD.created_at) THEN
                        RAISE EXCEPTION 'task_comments: task, author and created_at are immutable' USING ERRCODE = 'check_violation';
                    END IF;
                    IF OLD.deleted_at IS NOT NULL
                       AND (NEW.body, NEW.deleted_at, NEW.delete_reason) IS DISTINCT FROM (OLD.body, OLD.deleted_at, OLD.delete_reason) THEN
                        RAISE EXCEPTION 'task_comments: a deleted comment cannot be changed' USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER task_comments_guard BEFORE UPDATE OR DELETE ON task_comments
                    FOR EACH ROW EXECUTE FUNCTION htn_task_comments_guard();
                CREATE TRIGGER task_comments_no_truncate BEFORE TRUNCATE ON task_comments
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                CREATE FUNCTION htn_task_attachments_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'attachments are removed with a timestamp, never deleted' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF (NEW.id, NEW.task_id, NEW.file_id, NEW.file_name, NEW.uploaded_at, NEW.uploaded_by_id)
                       IS DISTINCT FROM (OLD.id, OLD.task_id, OLD.file_id, OLD.file_name, OLD.uploaded_at, OLD.uploaded_by_id) THEN
                        RAISE EXCEPTION 'task_attachments: only the removal stamp may change' USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER task_attachments_guard BEFORE UPDATE OR DELETE ON task_attachments
                    FOR EACH ROW EXECUTE FUNCTION htn_task_attachments_guard();
                CREATE TRIGGER task_attachments_no_truncate BEFORE TRUNCATE ON task_attachments
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                REVOKE UPDATE, DELETE, TRUNCATE ON task_activities FROM PUBLIC;
                REVOKE DELETE, TRUNCATE ON tasks, task_comments, task_attachments, points_awards FROM PUBLIC;
                -- Production runs the API as a non-owner role named htn_app (migrations run as the owner).
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'htn_app') THEN
                        REVOKE UPDATE, DELETE, TRUNCATE ON task_activities FROM htn_app;
                        REVOKE DELETE, TRUNCATE ON tasks, task_comments, task_attachments, points_awards FROM htn_app;
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // WARNING: rolling this back drops every task, its activity log and every points award ever made.
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS task_attachments_no_truncate ON task_attachments;
                DROP TRIGGER IF EXISTS task_attachments_guard ON task_attachments;
                DROP TRIGGER IF EXISTS task_comments_no_truncate ON task_comments;
                DROP TRIGGER IF EXISTS task_comments_guard ON task_comments;
                DROP TRIGGER IF EXISTS tasks_no_truncate ON tasks;
                DROP TRIGGER IF EXISTS tasks_guard ON tasks;
                DROP TRIGGER IF EXISTS points_awards_no_truncate ON points_awards;
                DROP TRIGGER IF EXISTS points_awards_guard ON points_awards;
                DROP TRIGGER IF EXISTS task_activities_no_truncate ON task_activities;
                DROP TRIGGER IF EXISTS task_activities_append_only ON task_activities;
                DROP FUNCTION IF EXISTS htn_task_attachments_guard();
                DROP FUNCTION IF EXISTS htn_task_comments_guard();
                DROP FUNCTION IF EXISTS htn_tasks_guard();
                DROP FUNCTION IF EXISTS htn_points_awards_guard();
                """);

            migrationBuilder.DropTable(
                name: "points_awards");

            migrationBuilder.DropTable(
                name: "task_activities");

            migrationBuilder.DropTable(
                name: "task_assignees");

            migrationBuilder.DropTable(
                name: "task_attachments");

            migrationBuilder.DropTable(
                name: "task_checklist_items");

            migrationBuilder.DropTable(
                name: "task_comments");

            migrationBuilder.DropTable(
                name: "tasks");

            migrationBuilder.DropColumn(
                name: "task_counter",
                table: "events");
        }
    }
}
