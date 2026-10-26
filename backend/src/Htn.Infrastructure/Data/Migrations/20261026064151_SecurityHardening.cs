using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SecurityHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "last_totp_time_step",
                table: "users",
                type: "bigint",
                nullable: true);

            // Keep only the newest open token per (user, purpose) so the unique index can be built.
            migrationBuilder.Sql("""
                UPDATE account_tokens t SET revoked_at = now()
                WHERE t.used_at IS NULL AND t.revoked_at IS NULL
                  AND EXISTS (
                    SELECT 1 FROM account_tokens n
                    WHERE n.user_id = t.user_id AND n.purpose = t.purpose
                      AND n.used_at IS NULL AND n.revoked_at IS NULL
                      AND (n.created_at, n.id) > (t.created_at, t.id));
                """);

            migrationBuilder.CreateIndex(
                name: "ux_account_tokens_open",
                table: "account_tokens",
                columns: new[] { "user_id", "purpose" },
                unique: true,
                filter: "used_at IS NULL AND revoked_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_account_tokens_open",
                table: "account_tokens");

            migrationBuilder.DropColumn(
                name: "last_totp_time_step",
                table: "users");
        }
    }
}
