using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Washtag.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceNormalizedName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "ShopServices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            // Backfill existing rows and remove any case-variant duplicates inserted
            // before this constraint existed, otherwise the unique index cannot build.
            migrationBuilder.Sql("UPDATE \"ShopServices\" SET \"NormalizedName\" = lower(\"Name\");");
            migrationBuilder.Sql(
                "DELETE FROM \"ShopServices\" a USING \"ShopServices\" b " +
                "WHERE a.\"ShopId\" = b.\"ShopId\" AND a.\"NormalizedName\" = b.\"NormalizedName\" AND a.\"Id\" > b.\"Id\";");

            migrationBuilder.CreateIndex(
                name: "IX_ShopServices_ShopId_NormalizedName",
                table: "ShopServices",
                columns: new[] { "ShopId", "NormalizedName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShopServices_ShopId_NormalizedName",
                table: "ShopServices");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "ShopServices");
        }
    }
}
