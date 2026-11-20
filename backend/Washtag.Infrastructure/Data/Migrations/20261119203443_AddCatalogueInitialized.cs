using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Washtag.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogueInitialized : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CatalogueInitialized",
                table: "Shops",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Shops that already have a catalogue must not be re-seeded after a delete.
            migrationBuilder.Sql(
                "UPDATE \"Shops\" SET \"CatalogueInitialized\" = true " +
                "WHERE EXISTS (SELECT 1 FROM \"ShopServices\" s WHERE s.\"ShopId\" = \"Shops\".\"Id\");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CatalogueInitialized",
                table: "Shops");
        }
    }
}
