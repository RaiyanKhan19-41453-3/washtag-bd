using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Washtag.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUnpaidInvoiceCounter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UnpaidInvoiceCount",
                table: "Shops",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Backfill existing shops from their current invoice state.
            migrationBuilder.Sql("""
                UPDATE "Shops" AS s
                SET "UnpaidInvoiceCount" = (
                    SELECT count(*)::int FROM "Invoices" AS i
                    WHERE i."ShopId" = s."Id" AND i."Status" IN (0, 2)
                );
                """);

            // Keep the counter in sync on every invoice write. Settlement can then
            // decide "is this the last unpaid invoice?" from the same row it locks,
            // without a SELECT ... FOR UPDATE or a cross-table snapshot read.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION washtag_maintain_unpaid_invoice_count()
                RETURNS trigger AS $$
                BEGIN
                  IF TG_OP = 'INSERT' THEN
                    IF NEW."Status" IN (0, 2) THEN
                      UPDATE "Shops" SET "UnpaidInvoiceCount" = "UnpaidInvoiceCount" + 1
                      WHERE "Id" = NEW."ShopId";
                    END IF;
                    RETURN NEW;
                  ELSIF TG_OP = 'DELETE' THEN
                    IF OLD."Status" IN (0, 2) THEN
                      UPDATE "Shops" SET "UnpaidInvoiceCount" = GREATEST("UnpaidInvoiceCount" - 1, 0)
                      WHERE "Id" = OLD."ShopId";
                    END IF;
                    RETURN OLD;
                  ELSE
                    IF OLD."Status" IN (0, 2) AND NEW."Status" NOT IN (0, 2) THEN
                      UPDATE "Shops" SET "UnpaidInvoiceCount" = GREATEST("UnpaidInvoiceCount" - 1, 0)
                      WHERE "Id" = NEW."ShopId";
                    ELSIF OLD."Status" NOT IN (0, 2) AND NEW."Status" IN (0, 2) THEN
                      UPDATE "Shops" SET "UnpaidInvoiceCount" = "UnpaidInvoiceCount" + 1
                      WHERE "Id" = NEW."ShopId";
                    END IF;
                    RETURN NEW;
                  END IF;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER "Invoices_UnpaidCount"
                AFTER INSERT OR UPDATE OR DELETE ON "Invoices"
                FOR EACH ROW EXECUTE FUNCTION washtag_maintain_unpaid_invoice_count();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"Invoices_UnpaidCount\" ON \"Invoices\";");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS washtag_maintain_unpaid_invoice_count();");
            migrationBuilder.DropColumn(
                name: "UnpaidInvoiceCount",
                table: "Shops");
        }
    }
}
