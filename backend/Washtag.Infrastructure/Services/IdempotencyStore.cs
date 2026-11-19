using Microsoft.Extensions.Caching.Distributed;
using Washtag.Application.Interfaces;

namespace Washtag.Infrastructure.Services;

/// <summary>Redis-backed idempotency keys so dedupe survives API restarts.</summary>
public class IdempotencyStore : IIdempotencyStore
{
    private readonly IDistributedCache _cache;

    public IdempotencyStore(IDistributedCache cache) => _cache = cache;

    private static string Key(string key, Guid shopId) => $"idem:{shopId}:{key}";

    public async Task<bool> TryClaimAsync(string key, Guid shopId, CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheKey = Key(key, shopId);
            if (await _cache.GetAsync(cacheKey, cancellationToken) != null) return false;
            await _cache.SetAsync(cacheKey, new byte[] { 1 }, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            }, cancellationToken);
            return true;
        }
        catch
        {
            // If the cache is unavailable, allow the write rather than blocking the shop.
            return true;
        }
    }

    public async Task ReleaseAsync(string key, Guid shopId, CancellationToken cancellationToken = default)
    {
        try { await _cache.RemoveAsync(Key(key, shopId), cancellationToken); }
        catch { /* best effort */ }
    }
}
