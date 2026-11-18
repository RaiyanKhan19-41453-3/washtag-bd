using System.Collections.Concurrent;
using Washtag.Application.Interfaces;

namespace Washtag.Infrastructure.Services;

public sealed class IdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<(Guid ShopId, string Key), DateTime> _claims = new();

    public bool TryClaim(string key, Guid shopId)
    {
        var now = DateTime.UtcNow;
        foreach (var claim in _claims.Where(c => c.Value < now.AddMinutes(-10)).ToList())
            _claims.TryRemove(claim.Key, out _);

        return _claims.TryAdd((shopId, key), now);
    }
}
