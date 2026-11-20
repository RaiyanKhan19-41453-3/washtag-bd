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

        // Atomic claim — a single conditional UPDATE both decides "is this the last
        // unpaid invoice?" (via the trigger-maintained counter on this very row) and
        // extends the term. The invoice UPDATE above already took a row lock on the
        // shop through the counter trigger, so concurrent settlements serialize on
        // that counter instead of an explicit SELECT ... FOR UPDATE. RETURNING reads
        // the plan from the same row version, defeating the stale-plan race. A
        // cancelled shop (status 3) never matches, so it is never resurrected.
        string? plan = null;
        DateTime renewal = now.AddMonths(1);
        await using (var claim = new NpgsqlCommand(
            """
            UPDATE "Shops" AS s
            SET "SubscriptionStatus" = CASE WHEN s."SubscriptionStatus" = 3 THEN 3 ELSE 1 END,
                "SubscriptionStartedAt" = CASE
                    WHEN s."SubscriptionStatus" = 3 OR s."SubscriptionEndsAt" > @now THEN s."SubscriptionStartedAt"
                    ELSE @now END,
                "SubscriptionEndsAt" = CASE
                    WHEN s."SubscriptionStatus" = 3 THEN s."SubscriptionEndsAt"
                    ELSE GREATEST(s."SubscriptionEndsAt", @now) + interval '1 month' END
            WHERE s."Id" = @shop
              AND s."SubscriptionStatus" <> 3
              AND s."UnpaidInvoiceCount" = 0
            RETURNING s."Plan", s."SubscriptionEndsAt"
            """, connection, transaction))
        {
            claim.Parameters.AddWithValue("now", now);
            claim.Parameters.AddWithValue("shop", shopId);
            await using var reader = await claim.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                plan = reader["Plan"] as string ?? "Starter";
                renewal = (DateTime)reader["SubscriptionEndsAt"];
            }
        }

        // Only the settlement that won the claim mints the renewal, using the
        // plan and new term it read atomically above.
        if (plan != null)
        {
            var monthlyPrice = Washtag.Domain.Plans.MonthlyPrice(plan);
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
