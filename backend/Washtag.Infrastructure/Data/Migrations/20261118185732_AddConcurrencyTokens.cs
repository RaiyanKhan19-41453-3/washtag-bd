using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Washtag.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Shops",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Orders",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            // Npgsql does not auto-generate bytea rowversion values; a trigger
            // guarantees the token changes on every update so EF detects conflicts.
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION washtag_set_rowversion() RETURNS trigger AS $$
                BEGIN
                    NEW.""RowVersion"" = convert_to(clock_timestamp()::text, 'UTF8');
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER ""Orders_RowVersion""
                    BEFORE UPDATE ON ""Orders""
                    FOR EACH ROW EXECUTE FUNCTION washtag_set_rowversion();

                CREATE TRIGGER ""Shops_RowVersion""
                    BEFORE UPDATE ON ""Shops""
                    FOR EACH ROW EXECUTE FUNCTION washtag_set_rowversion();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS ""Orders_RowVersion"" ON ""Orders"";
                DROP TRIGGER IF EXISTS ""Shops_RowVersion"" ON ""Shops"";
                DROP FUNCTION IF EXISTS washtag_set_rowversion();
            ");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Shops");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Orders");
        }
    }
}
