using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Washtag.Application.Interfaces;

namespace Washtag.Infrastructure.Services;

public class CacheService : ICacheService
{
    private readonly IDistributedCache _cache;

    public CacheService(IDistributedCache cache) => _cache = cache;

    public async Task<T?> GetAsync<T>(string key)
    {
        try
        {
            var bytes = await _cache.GetAsync(key);
            return bytes == null ? default : JsonSerializer.Deserialize<T>(bytes);
        }
        catch { return default; }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan expiration)
    {
        try
        {
            await _cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = expiration });
        }
        catch { /* cache is best-effort */ }
    }

    public async Task RemoveAsync(string key)
    {
        try { await _cache.RemoveAsync(key); }
        catch { /* cache is best-effort */ }
    }
}
