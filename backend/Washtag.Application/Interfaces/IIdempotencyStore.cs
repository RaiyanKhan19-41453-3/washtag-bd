namespace Washtag.Application.Interfaces;

/// <summary>Short-lived dedupe of write requests identified by an Idempotency-Key header.</summary>
public interface IIdempotencyStore
{
    Task<bool> TryClaimAsync(string key, Guid shopId, CancellationToken cancellationToken = default);
    Task ReleaseAsync(string key, Guid shopId, CancellationToken cancellationToken = default);
}
