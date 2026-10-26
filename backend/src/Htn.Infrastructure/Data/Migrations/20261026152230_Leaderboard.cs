using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Slice 4C, from the database review of the Tasks migration: four covering partial indexes for the leaderboard's
    /// three scopes (one event, the current Manila quarter across every event, all time), each INCLUDE-ing the columns
    /// the grouping aggregates so it is an index-only scan. The Tasks migration's narrower
    /// <c>(event_id, person_id)</c> and <c>(committee_id, person_id)</c> indexes are subsumed and dropped.
    /// Index-only: no table, column or data change, and reversible.
    /// </summary>
    public partial class Leaderboard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_points_awards_committee_id_person_id",
                table: "points_awards");

            migrationBuilder.DropIndex(
                name: "ix_points_awards_event_id_person_id",
                table: "points_awards");

            migrationBuilder.DropIndex(
                name: "ix_points_awards_person_id_awarded_at",
                table: "points_awards");

            migrationBuilder.CreateIndex(
                name: "ix_points_awards_awarded_at_person_id",
                table: "points_awards",
                columns: new[] { "awarded_at", "person_id" },
                filter: "reversed_at IS NULL")
                .Annotation("Npgsql:IndexInclude", new[] { "points", "role", "event_id", "committee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_points_awards_committee_id_awarded_at_person_id",
                table: "points_awards",
                columns: new[] { "committee_id", "awarded_at", "person_id" },
                filter: "reversed_at IS NULL")
                .Annotation("Npgsql:IndexInclude", new[] { "points", "role" });

            migrationBuilder.CreateIndex(
                name: "ix_points_awards_event_id_awarded_at_person_id",
                table: "points_awards",
                columns: new[] { "event_id", "awarded_at", "person_id" },
                filter: "reversed_at IS NULL")
                .Annotation("Npgsql:IndexInclude", new[] { "points", "role", "committee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_points_awards_person_id_awarded_at",
                table: "points_awards",
                columns: new[] { "person_id", "awarded_at" },
                filter: "reversed_at IS NULL")
                .Annotation("Npgsql:IndexInclude", new[] { "points", "event_id", "committee_id", "role" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_points_awards_awarded_at_person_id",
                table: "points_awards");

            migrationBuilder.DropIndex(
                name: "ix_points_awards_committee_id_awarded_at_person_id",
                table: "points_awards");

            migrationBuilder.DropIndex(
                name: "ix_points_awards_event_id_awarded_at_person_id",
                table: "points_awards");

            migrationBuilder.DropIndex(
                name: "ix_points_awards_person_id_awarded_at",
                table: "points_awards");

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
        }
    }
}
