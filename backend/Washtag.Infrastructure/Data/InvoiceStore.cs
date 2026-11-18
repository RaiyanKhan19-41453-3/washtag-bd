using Microsoft.Extensions.Configuration;
using Npgsql;
using Washtag.Application.Interfaces;
using Washtag.Domain.Enums;

namespace Washtag.Infrastructure.Data;

/// <summary>Claims an invoice once and performs the subscription rollover in one transaction.</summary>
public class InvoiceStore : IInvoiceStore
{
    private readonly string _connectionString;

    public InvoiceStore(IConfiguration configuration) =>
        _connectionString = configuration.GetConnectionString("Postgres") ?? string.Empty;

    public async Task<bool> SettleAsync(Guid shopId, Guid invoiceId, PaymentMethod method, string? reference,
        decimal monthlyPrice, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;

        await using (var update = new NpgsqlCommand(
            """
            UPDATE "Invoices"
            SET "Status" = 1, "PaidAt" = @now, "Method" = @method, "Reference" = @ref
            WHERE "Id" = @id AND "ShopId" = @shop AND "Status" IN (0, 2)
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("now", now);
            update.Parameters.AddWithValue("method", (int)method);
            update.Parameters.AddWithValue("ref", (object?)reference ?? DBNull.Value);
            update.Parameters.AddWithValue("id", invoiceId);
            update.Parameters.AddWithValue("shop", shopId);
            if (await update.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        await using (var count = new NpgsqlCommand(
            """
            SELECT count(*) FROM "Invoices"
            WHERE "ShopId" = @shop AND "Id" <> @id AND "Status" IN (0, 2)
            """, connection, transaction))
        {
            count.Parameters.AddWithValue("shop", shopId);
            count.Parameters.AddWithValue("id", invoiceId);
            var remaining = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken));
            if (remaining == 0)
            {
                var renewal = now.AddMonths(1);
                await using var shopUpdate = new NpgsqlCommand(
                    """
                    UPDATE "Shops"
                    SET "SubscriptionStatus" = 1, "SubscriptionStartedAt" = @now, "SubscriptionEndsAt" = @end
                    WHERE "Id" = @shop
                    """, connection, transaction);
                shopUpdate.Parameters.AddWithValue("now", now);
                shopUpdate.Parameters.AddWithValue("end", renewal);
                shopUpdate.Parameters.AddWithValue("shop", shopId);
                await shopUpdate.ExecuteNonQueryAsync(cancellationToken);

                await using var nextInvoice = new NpgsqlCommand(
                    """
                    INSERT INTO "Invoices" ("Id", "ShopId", "InvoiceNumber", "PeriodFrom", "PeriodTo", "Amount", "Status", "Method", "CreatedAt")
                    VALUES (@id, @shop, @number, @from, @to, @amount, 0, @method, @now)
                    """, connection, transaction);
                nextInvoice.Parameters.AddWithValue("id", Guid.NewGuid());
                nextInvoice.Parameters.AddWithValue("shop", shopId);
                nextInvoice.Parameters.AddWithValue("number", $"INV-{now:yyyyMM}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}");
                nextInvoice.Parameters.AddWithValue("from", now);
                nextInvoice.Parameters.AddWithValue("to", renewal);
                nextInvoice.Parameters.AddWithValue("amount", monthlyPrice);
                nextInvoice.Parameters.AddWithValue("method", (int)PaymentMethod.Cash);
                nextInvoice.Parameters.AddWithValue("now", now);
                await nextInvoice.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
