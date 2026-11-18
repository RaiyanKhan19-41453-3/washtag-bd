using System.Globalization;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Washtag.Application.DTOs;
using Washtag.Application.Interfaces;

namespace Washtag.Infrastructure.Services;

/// <summary>pgvector-backed document store. Uses raw Npgsql so no vector type mapping package is needed.</summary>
public class VectorStore : IVectorStore
{
    private readonly string _connectionString;

    public VectorStore(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=washtagdb;Username=washtag;Password=washtag_secret";
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            CREATE EXTENSION IF NOT EXISTS vector;
            CREATE TABLE IF NOT EXISTS shop_documents (
                id uuid PRIMARY KEY,
                shop_id uuid NOT NULL,
                doc_type text NOT NULL,
                ref_id uuid NOT NULL,
                content text NOT NULL,
                embedding vector(384) NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                UNIQUE (shop_id, doc_type, ref_id)
            );
            CREATE INDEX IF NOT EXISTS ix_shop_documents_shop ON shop_documents (shop_id);
            """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertAsync(Guid shopId, string docType, Guid refId, string content, float[] embedding,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO shop_documents (id, shop_id, doc_type, ref_id, content, embedding)
            VALUES (@id, @shop, @type, @ref, @content, @vector::vector)
            ON CONFLICT (shop_id, doc_type, ref_id)
            DO UPDATE SET content = EXCLUDED.content, embedding = EXCLUDED.embedding
            """, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("shop", shopId);
        command.Parameters.AddWithValue("type", docType);
        command.Parameters.AddWithValue("ref", refId);
        command.Parameters.AddWithValue("content", content);
        command.Parameters.AddWithValue("vector", Literal(embedding));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<List<DocumentMatch>> SearchAsync(Guid shopId, float[] embedding, int limit,
        CancellationToken cancellationToken = default)
    {
        var results = new List<DocumentMatch>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT doc_type, ref_id, content, 1 - (embedding <=> @vector::vector) AS similarity
            FROM shop_documents
            WHERE shop_id = @shop
            ORDER BY embedding <=> @vector::vector
            LIMIT @limit
            """, connection);
        command.Parameters.AddWithValue("shop", shopId);
        command.Parameters.AddWithValue("vector", Literal(embedding));
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 50));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new DocumentMatch
            {
                DocType = reader.GetString(0),
                RefId = reader.GetGuid(1),
                Content = reader.GetString(2),
                Similarity = reader.GetDouble(3)
            });
        }
        return results;
    }

    public async Task<int> CountAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM shop_documents WHERE shop_id = @shop", connection);
        command.Parameters.AddWithValue("shop", shopId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<List<DocumentRef>> GetRefsAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        var refs = new List<DocumentRef>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT doc_type, ref_id FROM shop_documents WHERE shop_id = @shop", connection);
        command.Parameters.AddWithValue("shop", shopId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            refs.Add(new DocumentRef { DocType = reader.GetString(0), RefId = reader.GetGuid(1) });
        return refs;
    }

    public async Task DeleteAsync(Guid shopId, string docType, Guid refId, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "DELETE FROM shop_documents WHERE shop_id = @shop AND doc_type = @type AND ref_id = @ref", connection);
        command.Parameters.AddWithValue("shop", shopId);
        command.Parameters.AddWithValue("type", docType);
        command.Parameters.AddWithValue("ref", refId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Literal(float[] embedding) =>
        "[" + string.Join(",", embedding.Select(v => v.ToString("0.######", CultureInfo.InvariantCulture))) + "]";
}
