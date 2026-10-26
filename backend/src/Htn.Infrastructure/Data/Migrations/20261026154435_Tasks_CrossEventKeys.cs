using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Phase 4 database review, correctness pass: the task tables are keyed to their event, so no row can straddle
    /// two events.
    /// <list type="bullet">
    /// <item><c>ak_tasks_id_event_id</c> and <c>ak_event_volunteers_id_event_id</c>, the alternate keys the composite
    /// foreign keys below point at.</item>
    /// <item><c>points_awards</c> is keyed to <c>tasks (id, event_id)</c> and <c>committees (id, event_id)</c>, so an
    /// award snapshot can never name a task or a committee of another event.</item>
    /// <item><c>task_assignees</c> carries <c>event_id</c> (backfilled from its task) and is keyed to
    /// <c>tasks (id, event_id)</c> and <c>event_volunteers (id, event_id)</c>: a volunteer of another event can no
    /// longer be put on a card.</item>
    /// <item><c>task_comments.author_id</c> becomes <c>author_user_id</c>, with the foreign key to <c>users</c> that
    /// says out loud it is a user id and not one of the person ids beside it.</item>
    /// <item>The three redundant <c>(committee_id, event_id)</c> foreign-key indexes the review names are dropped;
    /// <c>HtnForeignKeyIndexConvention</c> keeps them out of the model from now on.</item>
    /// </list>
    /// </summary>
    public partial class Tasks_CrossEventKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_points_awards_committees_committee_id",
                table: "points_awards");

            migrationBuilder.DropForeignKey(
                name: "fk_points_awards_tasks_task_id",
                table: "points_awards");

            migrationBuilder.DropForeignKey(
                name: "fk_task_assignees_event_volunteers_volunteer_id",
                table: "task_assignees");

            migrationBuilder.DropForeignKey(
                name: "fk_task_assignees_tasks_task_id",
                table: "task_assignees");

            migrationBuilder.DropIndex(
                name: "ix_tasks_committee_id_event_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_task_assignees_volunteer_id",
                table: "task_assignees");

            migrationBuilder.DropIndex(
                name: "ix_event_volunteers_committee_id_event_id",
                table: "event_volunteers");

            migrationBuilder.DropIndex(
                name: "ix_committee_members_committee_id_event_id",
                table: "committee_members");

            migrationBuilder.RenameColumn(
                name: "author_id",
                table: "task_comments",
                newName: "author_user_id");

            // Added nullable, backfilled from the parent task, then tightened to NOT NULL: no existing row is left
            // behind and no zero-guid default ever reaches the composite foreign key.
            migrationBuilder.AddColumn<Guid>(
                name: "event_id",
                table: "task_assignees",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                @"UPDATE task_assignees a SET event_id = t.event_id FROM tasks t WHERE t.id = a.task_id;
                  ALTER TABLE task_assignees ALTER COLUMN event_id SET NOT NULL;");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_tasks_id_event_id",
                table: "tasks",
                columns: new[] { "id", "event_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_event_volunteers_id_event_id",
                table: "event_volunteers",
                columns: new[] { "id", "event_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_comments_author_user_id",
                table: "task_comments",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_assignees_volunteer_id_event_id",
                table: "task_assignees",
                columns: new[] { "volunteer_id", "event_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_points_awards_committees_committee_id_event_id",
                table: "points_awards",
                columns: new[] { "committee_id", "event_id" },
                principalTable: "committees",
                principalColumns: new[] { "id", "event_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_points_awards_tasks_task_id_event_id",
                table: "points_awards",
                columns: new[] { "task_id", "event_id" },
                principalTable: "tasks",
                principalColumns: new[] { "id", "event_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_task_assignees_event_volunteers_volunteer_id_event_id",
                table: "task_assignees",
                columns: new[] { "volunteer_id", "event_id" },
                principalTable: "event_volunteers",
                principalColumns: new[] { "id", "event_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_task_assignees_tasks_task_id_event_id",
                table: "task_assignees",
                columns: new[] { "task_id", "event_id" },
                principalTable: "tasks",
                principalColumns: new[] { "id", "event_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_task_comments_users_author_user_id",
                table: "task_comments",
                column: "author_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // The comment guard names the author column, and plpgsql resolves that at run time: it is re-created
            // here so the renamed column keeps the append-only rules.
            migrationBuilder.Sql(
                @"
                CREATE OR REPLACE FUNCTION htn_task_comments_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'comments are deleted with a reason (soft), never removed'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF (NEW.id, NEW.task_id, NEW.author_user_id, NEW.created_at)
                       IS DISTINCT FROM (OLD.id, OLD.task_id, OLD.author_user_id, OLD.created_at) THEN
                        RAISE EXCEPTION 'task_comments: task, author and created_at are immutable'
                            USING ERRCODE = 'check_violation';
                    END IF;
                    IF OLD.deleted_at IS NOT NULL
                       AND (NEW.body, NEW.deleted_at, NEW.delete_reason)
                           IS DISTINCT FROM (OLD.body, OLD.deleted_at, OLD.delete_reason) THEN
                        RAISE EXCEPTION 'task_comments: a deleted comment cannot be changed'
                            USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_points_awards_committees_committee_id_event_id",
                table: "points_awards");

            migrationBuilder.DropForeignKey(
                name: "fk_points_awards_tasks_task_id_event_id",
                table: "points_awards");

            migrationBuilder.DropForeignKey(
                name: "fk_task_assignees_event_volunteers_volunteer_id_event_id",
                table: "task_assignees");

            migrationBuilder.DropForeignKey(
                name: "fk_task_assignees_tasks_task_id_event_id",
                table: "task_assignees");

            migrationBuilder.DropForeignKey(
                name: "fk_task_comments_users_author_user_id",
                table: "task_comments");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_tasks_id_event_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_task_comments_author_user_id",
                table: "task_comments");

            migrationBuilder.DropIndex(
                name: "ix_task_assignees_volunteer_id_event_id",
                table: "task_assignees");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_event_volunteers_id_event_id",
                table: "event_volunteers");

            migrationBuilder.DropColumn(
                name: "event_id",
                table: "task_assignees");

            migrationBuilder.RenameColumn(
                name: "author_user_id",
                table: "task_comments",
                newName: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_committee_id_event_id",
                table: "tasks",
                columns: new[] { "committee_id", "event_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_assignees_volunteer_id",
                table: "task_assignees",
                column: "volunteer_id");

            migrationBuilder.CreateIndex(
                name: "ix_event_volunteers_committee_id_event_id",
                table: "event_volunteers",
                columns: new[] { "committee_id", "event_id" });

            migrationBuilder.CreateIndex(
                name: "ix_committee_members_committee_id_event_id",
                table: "committee_members",
                columns: new[] { "committee_id", "event_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_points_awards_committees_committee_id",
                table: "points_awards",
                column: "committee_id",
                principalTable: "committees",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_points_awards_tasks_task_id",
                table: "points_awards",
                column: "task_id",
                principalTable: "tasks",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_task_assignees_event_volunteers_volunteer_id",
                table: "task_assignees",
                column: "volunteer_id",
                principalTable: "event_volunteers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_task_assignees_tasks_task_id",
                table: "task_assignees",
                column: "task_id",
                principalTable: "tasks",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // The comment guard names the author column, and plpgsql resolves that at run time: it is re-created
            // here so the renamed column keeps the append-only rules.
            migrationBuilder.Sql(
                @"
                CREATE OR REPLACE FUNCTION htn_task_comments_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'comments are deleted with a reason (soft), never removed'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF (NEW.id, NEW.task_id, NEW.author_id, NEW.created_at)
                       IS DISTINCT FROM (OLD.id, OLD.task_id, OLD.author_id, OLD.created_at) THEN
                        RAISE EXCEPTION 'task_comments: task, author and created_at are immutable'
                            USING ERRCODE = 'check_violation';
                    END IF;
                    IF OLD.deleted_at IS NOT NULL
                       AND (NEW.body, NEW.deleted_at, NEW.delete_reason)
                           IS DISTINCT FROM (OLD.body, OLD.deleted_at, OLD.delete_reason) THEN
                        RAISE EXCEPTION 'task_comments: a deleted comment cannot be changed'
                            USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                ");
        }
    }
}
