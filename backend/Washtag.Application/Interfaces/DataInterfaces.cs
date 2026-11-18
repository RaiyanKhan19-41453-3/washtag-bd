namespace Washtag.Application.Interfaces;

/// <summary>Atomic "insert if missing" for shop customers.</summary>
public interface ICustomerStore
{
    Task EnsureAsync(Guid shopId, string name, string phone, DateTime createdAt, CancellationToken cancellationToken = default);
}

/// <summary>Atomically claims a refresh token so rotation cannot be replayed.</summary>
public interface IRefreshTokenStore
{
    Task<Guid?> TryClaimAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task SetReplacedByAsync(string oldHash, string newHash, CancellationToken cancellationToken = default);
}
