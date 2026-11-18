using Microsoft.Extensions.Configuration;
using Npgsql;
using Washtag.Application.Interfaces;
using Washtag.Domain.Enums;

namespace Washtag.Infrastructure.Data;

/// <summary>
/// Uses a conditional UPDATE inside a transaction so concurrent payments cannot
/// overshoot the outstanding balance or leave payment rows inconsistent.
/// </summary>
public class PaymentStore : IPaymentStore
{
    private readonly string _connectionString;

    public PaymentStore(IConfiguration configuration) =>
        _connectionString = configuration.GetConnectionString("Postgres") ?? string.Empty;

    public async Task<bool> RecordAsync(Guid shopId, Guid orderId, decimal amount, PaymentMethod method,
        string? reference, string recordedBy, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var update = new NpgsqlCommand(
            """
            UPDATE "Orders"
            SET "PaidAmount" = "PaidAmount" + @amount,
                "PaymentStatus" = CASE WHEN "PaidAmount" + @amount >= "TotalAmount" THEN 2 ELSE 1 END,
                "UpdatedAt" = @now
            WHERE "Id" = @id AND "ShopId" = @shop
              AND "Status" NOT IN (4, 5)
              AND "TotalAmount" - "PaidAmount" >= @amount
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("amount", amount);
            update.Parameters.AddWithValue("now", DateTime.UtcNow);
            update.Parameters.AddWithValue("id", orderId);
            update.Parameters.AddWithValue("shop", shopId);
            if (await update.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO "Payments" ("Id", "OrderId", "Amount", "Method", "Reference", "RecordedBy", "CreatedAt")
            VALUES (@id, @order, @amount, @method, @reference, @recorded, @now)
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("id", Guid.NewGuid());
            insert.Parameters.AddWithValue("order", orderId);
            insert.Parameters.AddWithValue("amount", amount);
            insert.Parameters.AddWithValue("method", (int)method);
            insert.Parameters.AddWithValue("reference", (object?)reference ?? DBNull.Value);
            insert.Parameters.AddWithValue("recorded", recordedBy);
            insert.Parameters.AddWithValue("now", DateTime.UtcNow);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
