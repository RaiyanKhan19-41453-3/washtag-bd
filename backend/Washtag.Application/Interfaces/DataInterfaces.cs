using Washtag.Domain.Enums;

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
    Task PurgeExpiredAsync(CancellationToken cancellationToken = default);
}

/// <summary>Atomically records a payment without ever exceeding the order balance.</summary>
public interface IPaymentStore
{
    Task<bool> RecordAsync(Guid shopId, Guid orderId, decimal amount, PaymentMethod method,
        string? reference, string recordedBy, CancellationToken cancellationToken = default);
}
