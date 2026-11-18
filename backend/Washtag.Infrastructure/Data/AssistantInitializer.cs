using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Washtag.Application.Interfaces;

namespace Washtag.Infrastructure.Data;

public static class AssistantInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AssistantInitializer");
        try
        {
            var vectors = scope.ServiceProvider.GetRequiredService<IVectorStore>();
            await vectors.EnsureSchemaAsync();

            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var shopIds = await db.Shops.Select(s => s.Id).ToListAsync();
            var indexer = scope.ServiceProvider.GetRequiredService<IDocumentIndexer>();

            var documents = 0;
            foreach (var shopId in shopIds)
            {
                await indexer.ReindexShopAsync(shopId);
                documents += await vectors.CountAsync(shopId);
            }
            logger.LogInformation("AI assistant index ready: {Documents} document(s) across {Shops} shop(s).", documents, shopIds.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AI assistant index unavailable; answers will use database facts only.");
        }
    }
}
