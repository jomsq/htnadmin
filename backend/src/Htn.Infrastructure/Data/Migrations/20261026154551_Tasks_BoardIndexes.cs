using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Phase 4 database review, index pass. The board reads one event's live cards in display order, so it gets a
    /// partial index of exactly that shape, plus one for the committee chip and one for the archive view; the
    /// assignee index gains the task id so "My tasks" is an index-only lookup. Index-only: no table, column or data
    /// change, and reversible.
    /// </summary>
    public partial class Tasks_BoardIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_task_assignees_person_id",
                table: "task_assignees");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_event_id_archived_at",
                table: "tasks",
                columns: new[] { "event_id", "archived_at" },
                filter: "is_archived");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_event_id_committee_id_sort_order",
                table: "tasks",
                columns: new[] { "event_id", "committee_id", "sort_order" },
                filter: "NOT is_archived");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_event_id_sort_order_created_at",
                table: "tasks",
                columns: new[] { "event_id", "sort_order", "created_at" },
                filter: "NOT is_archived");

            migrationBuilder.CreateIndex(
                name: "ix_task_assignees_person_id_task_id",
                table: "task_assignees",
                columns: new[] { "person_id", "task_id" },
                filter: "person_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tasks_event_id_archived_at",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_tasks_event_id_committee_id_sort_order",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_tasks_event_id_sort_order_created_at",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_task_assignees_person_id_task_id",
                table: "task_assignees");

            migrationBuilder.CreateIndex(
                name: "ix_task_assignees_person_id",
                table: "task_assignees",
                column: "person_id",
                filter: "person_id IS NOT NULL");
        }
    }
}
