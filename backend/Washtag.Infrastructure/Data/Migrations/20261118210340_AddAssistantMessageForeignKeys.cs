using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Washtag.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistantMessageForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AssistantMessages_UserId",
                table: "AssistantMessages",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AssistantMessages_Shops_ShopId",
                table: "AssistantMessages",
                column: "ShopId",
                principalTable: "Shops",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AssistantMessages_Users_UserId",
                table: "AssistantMessages",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AssistantMessages_Shops_ShopId",
                table: "AssistantMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_AssistantMessages_Users_UserId",
                table: "AssistantMessages");

            migrationBuilder.DropIndex(
                name: "IX_AssistantMessages_UserId",
                table: "AssistantMessages");
        }
    }
}
