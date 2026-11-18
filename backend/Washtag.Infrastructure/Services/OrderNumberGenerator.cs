using Microsoft.Extensions.Configuration;
using Npgsql;
using Washtag.Application.Interfaces;
using Washtag.Domain.Exceptions;

namespace Washtag.Infrastructure.Services;

/// <summary>Uses an atomic UPDATE ... RETURNING so concurrent creates cannot duplicate a number.</summary>
public class OrderNumberGenerator : IOrderNumberGenerator
{
    private readonly string _connectionString;

    public OrderNumberGenerator(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=washtagdb;Username=washtag;Password=washtag_secret";
    }

    public async Task<string> NextAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE \"Shops\" SET \"OrderSequence\" = \"OrderSequence\" + 1 " +
            "WHERE \"Id\" = @shop RETURNING \"OrderSequence\"", connection);
        command.Parameters.AddWithValue("shop", shopId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result == null) throw new AppException("Shop not found.");
        return $"WT-{Convert.ToInt32(result):D5}";
    }
}
