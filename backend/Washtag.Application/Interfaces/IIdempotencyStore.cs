namespace Washtag.Application.Interfaces;

/// <summary>Short-lived dedupe of write requests identified by an Idempotency-Key header.</summary>
public interface IIdempotencyStore
{
    bool TryClaim(string key, Guid shopId);
}
