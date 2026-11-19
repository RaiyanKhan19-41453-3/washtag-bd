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
    private readonly IInvoiceStore _invoices;
    private readonly MaintenanceOptions _options;

    public MaintenanceService(IAppDbContext db, IRefreshTokenStore tokens,
        IDocumentIndexer indexer, IInvoiceStore invoices, MaintenanceOptions options)
    {
        _db = db;
        _tokens = tokens;
        _indexer = indexer;
        _invoices = invoices;
        _options = options;
    }

    public async Task<MaintenanceResultDto> RunAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var result = new MaintenanceResultDto { RanAt = now };

        // Each step is isolated: a failing step must not block the others or make
        // later runs skip them.
        try
        {
            // One atomic transition shared with the lazy request-time paths.
            var deadlines = await _invoices.ApplyDeadlinesAsync(now, null, cancellationToken);
            result.OverdueInvoices = deadlines.OverdueInvoices;
            result.ExpiredSubscriptions = deadlines.ShopsFlagged;
            result.ShopsMarkedPastDue = deadlines.ShopsFlagged;
        }
        catch (Exception ex) { result.Errors.Add($"Billing deadlines: {ex.Message}"); }

        try
        {
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
        }
        catch (Exception ex) { result.Errors.Add($"Chat retention: {ex.Message}"); }

        try
        {
            await _tokens.PurgeExpiredAsync(cancellationToken);
            result.TokensPurged = true;
        }
        catch (Exception ex) { result.Errors.Add($"Token purge: {ex.Message}"); }

        try
        {
            var shopIds = await _db.Shops.Select(s => s.Id).ToListAsync(cancellationToken);
            foreach (var shopId in shopIds)
            {
                try { await _indexer.ReindexShopAsync(shopId, cancellationToken); result.ReindexedShops++; }
                catch (Exception ex) { result.Errors.Add($"Reindex {shopId}: {ex.Message}"); }
            }
        }
        catch (Exception ex) { result.Errors.Add($"Reindex: {ex.Message}"); }

        return result;
    }
}
