using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Washtag.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReportingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Shops_SubscriptionStatus_SubscriptionEndsAt",
                table: "Shops",
                columns: new[] { "SubscriptionStatus", "SubscriptionEndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ShopId_Status",
                table: "Invoices",
                columns: new[] { "ShopId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_Status_PeriodTo",
                table: "Invoices",
                columns: new[] { "Status", "PeriodTo" });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantMessages_CreatedAt",
                table: "AssistantMessages",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Shops_SubscriptionStatus_SubscriptionEndsAt",
                table: "Shops");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_ShopId_Status",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_Status_PeriodTo",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_AssistantMessages_CreatedAt",
                table: "AssistantMessages");
        }
    }
}
