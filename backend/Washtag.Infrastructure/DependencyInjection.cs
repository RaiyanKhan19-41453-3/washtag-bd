using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Washtag.Application.Interfaces;
using Washtag.Infrastructure.Data;
using Washtag.Infrastructure.Services;

namespace Washtag.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres")));
        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());

        services.AddStackExchangeRedisCache(options =>
            options.Configuration = configuration.GetConnectionString("Redis") ?? "localhost:6379");
        services.AddScoped<ICacheService, CacheService>();
        services.AddScoped<IQrCodeService, QrCodeService>();

        services.AddHttpClient<AiProvider>(client => client.Timeout = TimeSpan.FromSeconds(25));
        services.AddScoped<IAiProvider>(provider => provider.GetRequiredService<AiProvider>());
        services.AddScoped<IVectorStore, VectorStore>();
        services.AddScoped<IDocumentIndexer, DocumentIndexer>();

        return services;
    }
}
