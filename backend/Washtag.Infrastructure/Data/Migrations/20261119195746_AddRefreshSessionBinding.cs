using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Washtag.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshSessionBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SessionHash",
                table: "RefreshTokens",
                type: "text",
                nullable: true);

            // Pre-upgrade tokens have no session binding. Force one re-login so every
            // live token is bound (and a stolen legacy token cannot claim a successor).
            migrationBuilder.Sql("DELETE FROM \"RefreshTokens\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SessionHash",
                table: "RefreshTokens");
        }
    }
}
