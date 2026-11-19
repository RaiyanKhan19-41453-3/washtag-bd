using Microsoft.EntityFrameworkCore;
using Washtag.Application.DTOs;
using Washtag.Application.Interfaces;
using Washtag.Domain.Enums;

namespace Washtag.Application.Services;

/// <summary>
/// Periodic housekeeping that used to happen lazily on the first request that touched it.
/// Every step is safe to repeat and the whole run is idempotent.
/// </summary>
public sealed class MaintenanceService : IMaintenanceService
{
    private readonly IAppDbContext _db;
    private readonly IRefreshTokenStore _tokens;
    private readonly IDocumentIndexer _indexer;
    private readonly MaintenanceOptions _options;

    public MaintenanceService(IAppDbContext db, IRefreshTokenStore tokens,
        IDocumentIndexer indexer, MaintenanceOptions options)
    {
        _db = db;
        _tokens = tokens;
        _indexer = indexer;
        _options = options;
    }

    public async Task<MaintenanceResultDto> RunAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var result = new MaintenanceResultDto { RanAt = now };

        // Billing periods that ended without payment become overdue.
        var overdue = await _db.Invoices
            .Where(i => i.Status == InvoiceStatus.Pending && i.PeriodTo < now)
            .ToListAsync(cancellationToken);
        foreach (var invoice in overdue) invoice.Status = InvoiceStatus.Overdue;
        result.OverdueInvoices = overdue.Count;

        // Subscriptions whose term elapsed are expired, even if no invoice flagged them.
        var expired = await _db.Shops
            .Where(s => s.IsActive && s.SubscriptionEndsAt <= now &&
                        (s.SubscriptionStatus == SubscriptionStatus.Trial ||
                         s.SubscriptionStatus == SubscriptionStatus.Active))
            .ToListAsync(cancellationToken);
        result.ExpiredSubscriptions = expired.Count;

        // One shop can be affected by both an overdue invoice and an expired term; count it once.
        var affectedIds = overdue.Select(i => i.ShopId).Concat(expired.Select(s => s.Id)).ToHashSet();
        var toFlag = await _db.Shops
            .Where(s => s.IsActive && affectedIds.Contains(s.Id) &&
                        (s.SubscriptionStatus == SubscriptionStatus.Trial ||
                         s.SubscriptionStatus == SubscriptionStatus.Active))
            .ToListAsync(cancellationToken);
        foreach (var shop in toFlag) shop.SubscriptionStatus = SubscriptionStatus.PastDue;
        result.ShopsMarkedPastDue = toFlag.Count;

        if (overdue.Count > 0 || toFlag.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);

        // Retention: old assistant conversations are dropped in bulk instead of one chat at a time.
        var cutoff = now.AddDays(-_options.ChatRetentionDays);
        var stale = await _db.AssistantMessages
            .Where(m => m.CreatedAt < cutoff)
            .ToListAsync(cancellationToken);
        if (stale.Count > 0)
        {
            _db.AssistantMessages.RemoveRange(stale);
            await _db.SaveChangesAsync(cancellationToken);
            result.PrunedMessages = stale.Count;
        }

        // Expired/old refresh tokens (30-day grace) are removed by the atomic token store.
        await _tokens.PurgeExpiredAsync(cancellationToken);
        result.TokensPurged = true;

        // Reindexing rewrites current documents and prunes rows whose order/customer is gone.
        var shopIds = await _db.Shops.Select(s => s.Id).ToListAsync(cancellationToken);
        foreach (var shopId in shopIds)
        {
            await _indexer.ReindexShopAsync(shopId, cancellationToken);
            result.ReindexedShops++;
        }

        return result;
    }
}
