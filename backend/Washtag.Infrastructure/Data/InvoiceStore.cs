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
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;

        // Serialize settlements per shop. Without this, two concurrent payments can
        // both see the other's invoice as unpaid and neither creates the renewal.
        await using (var lockShop = new NpgsqlCommand(
            "SELECT 1 FROM \"Shops\" WHERE \"Id\" = @shop FOR UPDATE", connection, transaction))
        {
            lockShop.Parameters.AddWithValue("shop", shopId);
            await lockShop.ExecuteNonQueryAsync(cancellationToken);
        }

        // Read the price under the same lock: a concurrent plan change must not be
        // overtaken by a renewal invoice priced with the old plan.
        string plan;
        await using (var readPlan = new NpgsqlCommand(
            "SELECT \"Plan\" FROM \"Shops\" WHERE \"Id\" = @shop", connection, transaction))
        {
            readPlan.Parameters.AddWithValue("shop", shopId);
            plan = (await readPlan.ExecuteScalarAsync(cancellationToken) as string) ?? "Starter";
        }
        var monthlyPrice = Washtag.Domain.Plans.MonthlyPrice(plan);

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
                // A cancelled subscription is terminal. Paying an old invoice must not
                // silently resurrect it or mint a fresh billing period. For an active
                // shop the term must never shrink: extend from the greater of the current
                // end and now, so prepaid time is never revoked by a payment.
                DateTime renewal;
                await using (var shopUpdate = new NpgsqlCommand(
                    """
                    UPDATE "Shops"
                    SET "SubscriptionStatus" = CASE WHEN "SubscriptionStatus" = 3 THEN 3 ELSE 1 END,
                        "SubscriptionStartedAt" = CASE
                            WHEN "SubscriptionStatus" = 3 OR "SubscriptionEndsAt" > @now THEN "SubscriptionStartedAt"
                            ELSE @now END,
                        "SubscriptionEndsAt" = CASE
                            WHEN "SubscriptionStatus" = 3 THEN "SubscriptionEndsAt"
                            ELSE GREATEST("SubscriptionEndsAt", @now) + interval '1 month' END
                    WHERE "Id" = @shop
                    RETURNING "SubscriptionEndsAt"
                    """, connection, transaction))
                {
                    shopUpdate.Parameters.AddWithValue("now", now);
                    shopUpdate.Parameters.AddWithValue("shop", shopId);
                    renewal = (DateTime)(await shopUpdate.ExecuteScalarAsync(cancellationToken)
                        ?? now.AddMonths(1));
                }

                await using var nextInvoice = new NpgsqlCommand(
                    """
                    INSERT INTO "Invoices" ("Id", "ShopId", "InvoiceNumber", "PeriodFrom", "PeriodTo", "Amount", "Status", "Method", "CreatedAt")
                    SELECT @id, @shop, @number, @from, @to, @amount, 0, @method, @now
                    FROM "Shops" WHERE "Id" = @shop AND "SubscriptionStatus" <> 3
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

    public async Task EnsureCurrentInvoiceAsync(Guid shopId, decimal monthlyPrice, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var from = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO "Invoices" ("Id", "ShopId", "InvoiceNumber", "PeriodFrom", "PeriodTo", "Amount", "Status", "Method", "CreatedAt")
            SELECT @id, @shop, @number, @from, @to, @amount, 0, 0, @now
            WHERE NOT EXISTS (
                SELECT 1 FROM "Invoices"
                WHERE "ShopId" = @shop AND "Status" IN (0, 2) AND "PeriodTo" > @now
            )
            """, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("shop", shopId);
        command.Parameters.AddWithValue("number", $"INV-{from:yyyyMM}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}");
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", from.AddMonths(1));
        command.Parameters.AddWithValue("amount", monthlyPrice);
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<BillingDeadlineResult> ApplyDeadlinesAsync(DateTime now, Guid? shopId = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var overdue = new NpgsqlCommand(
            """
            UPDATE "Invoices" SET "Status" = 2
            WHERE "Status" = 0 AND "PeriodTo" <= @now AND (@shop::uuid IS NULL OR "ShopId" = @shop)
            """, connection, transaction);
        overdue.Parameters.AddWithValue("now", now);
        overdue.Parameters.AddWithValue("shop", (object?)shopId ?? DBNull.Value);
        var overdueCount = await overdue.ExecuteNonQueryAsync(cancellationToken);

        // The subscription end date is the single source of truth for entitlement,
        // matching SubscriptionPolicy.IsLocked exactly. Settlement always moves the
        // end date forward, so a paid current period can never be flagged here.
        await using var flag = new NpgsqlCommand(
            """
            UPDATE "Shops" AS s SET "SubscriptionStatus" = 2
            WHERE s."IsActive" = true
              AND s."SubscriptionStatus" IN (0, 1)
              AND s."SubscriptionEndsAt" <= @now
              AND (@shop::uuid IS NULL OR s."Id" = @shop)
            """, connection, transaction);
        flag.Parameters.AddWithValue("now", now);
        flag.Parameters.AddWithValue("shop", (object?)shopId ?? DBNull.Value);
        var flagged = await flag.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new BillingDeadlineResult(overdueCount, flagged);
    }
}
