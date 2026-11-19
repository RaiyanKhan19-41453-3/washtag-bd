using DotNet.Testcontainers.Containers;
using Testcontainers.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Washtag.Application;
using Washtag.Application.Interfaces;
using Washtag.Domain.Entities;
using Washtag.Domain.Enums;
using Washtag.Infrastructure;
using Washtag.Infrastructure.Data;
using Xunit;

namespace Washtag.Integration.Tests;

/// <summary>A real pgvector-backed Postgres, migrated with the production migrations.</summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly IContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("washtagdb")
        .WithUsername("washtag")
        .WithPassword("washtag_secret")
        .Build();

    public IServiceProvider Services { get; private set; } = null!;
    public string ConnectionString =>
        $"Host={_postgres.Hostname};Port={_postgres.GetMappedPublicPort(5432)};" +
        "Database=washtagdb;Username=washtag;Password=washtag_secret;SSL Mode=Disable";

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = ConnectionString,
            ["OPENAI_API_KEY"] = string.Empty,
            ["AI_GATEWAY_TOKEN"] = string.Empty
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddApplication();
        services.AddInfrastructure(configuration);
        // Replace the Redis cache and API-only services with deterministic fakes.
        services.AddSingleton<ICacheService, MemoryCacheStub>();
        services.AddSingleton<ITrackingUrlProvider, TrackingUrlStub>();
        services.AddSingleton<IRealtimeNotifier, NoopNotifier>();
        Services = services.BuildServiceProvider();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<IVectorStore>().EnsureSchemaAsync();
    }

    public async Task DisposeAsync()
    {
        if (Services is IDisposable disposable) disposable.Dispose();
        await _postgres.DisposeAsync();
    }

    public IServiceScope Scope() => Services.CreateScope();

    public async Task<Shop> NewShopAsync(string? plan = "Starter", DateTime? endsAt = null)
    {
        using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid();
        var shop = new Shop
        {
            Id = id,
            Name = "Integration Shop",
            OwnerName = "Owner",
            Email = $"shop-{id}@test.com",
            Phone = "1",
            Plan = plan ?? "Starter",
            SubscriptionStatus = SubscriptionStatus.Trial,
            SubscriptionStartedAt = DateTime.UtcNow,
            SubscriptionEndsAt = endsAt ?? DateTime.UtcNow.AddMonths(1),
            CreatedAt = DateTime.UtcNow
        };
        db.Shops.Add(shop);
        await db.SaveChangesAsync();
        return shop;
    }
}

[CollectionDefinition("database", DisableParallelization = true)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture> { }

internal sealed class MemoryCacheStub : ICacheService
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    public Task<T?> GetAsync<T>(string key) =>
        Task.FromResult(_values.TryGetValue(key, out var value) && value is T typed ? typed : default);
    public Task SetAsync<T>(string key, T value, TimeSpan expiration)
    {
        _values[key] = value;
        return Task.CompletedTask;
    }
    public Task RemoveAsync(string key)
    {
        _values.Remove(key);
        return Task.CompletedTask;
    }
}

internal sealed class TrackingUrlStub : ITrackingUrlProvider
{
    public string Build(Guid token) => $"https://example.test/track/{token}";
}

internal sealed class NoopNotifier : IRealtimeNotifier
{
    public Task OrderUpdatedAsync(Order order, object ownerPayload, object customerPayload) => Task.CompletedTask;
}
