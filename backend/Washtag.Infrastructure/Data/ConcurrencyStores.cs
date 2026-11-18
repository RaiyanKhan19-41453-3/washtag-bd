using Microsoft.Extensions.Configuration;
using Npgsql;
using Washtag.Application.Interfaces;

namespace Washtag.Infrastructure.Data;

public class CustomerStore : ICustomerStore
{
    private readonly string _connectionString;
    public CustomerStore(IConfiguration configuration) =>
        _connectionString = configuration.GetConnectionString("Postgres") ?? string.Empty;

    public async Task EnsureAsync(Guid shopId, string name, string phone, DateTime createdAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO "Customers" ("Id", "ShopId", "Name", "Phone", "CreatedAt")
            VALUES (@id, @shop, @name, @phone, @created)
            ON CONFLICT ("ShopId", "Phone") DO NOTHING
            """, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("shop", shopId);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("phone", phone);
        command.Parameters.AddWithValue("created", createdAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public class RefreshTokenStore : IRefreshTokenStore
{
    private readonly string _connectionString;
    public RefreshTokenStore(IConfiguration configuration) =>
        _connectionString = configuration.GetConnectionString("Postgres") ?? string.Empty;

    public async Task<Guid?> TryClaimAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE "RefreshTokens" SET "RevokedAt" = now()
            WHERE "TokenHash" = @hash AND "RevokedAt" IS NULL AND "ExpiresAt" > now()
            RETURNING "UserId"
            """, connection);
        command.Parameters.AddWithValue("hash", tokenHash);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result == null || result == DBNull.Value ? null : (Guid)result;
    }

    public async Task SetReplacedByAsync(string oldHash, string newHash, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE \"RefreshTokens\" SET \"ReplacedBy\" = @new WHERE \"TokenHash\" = @old", connection);
        command.Parameters.AddWithValue("new", newHash);
        command.Parameters.AddWithValue("old", oldHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "DELETE FROM \"RefreshTokens\" WHERE \"ExpiresAt\" < now() - interval '30 days'", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
