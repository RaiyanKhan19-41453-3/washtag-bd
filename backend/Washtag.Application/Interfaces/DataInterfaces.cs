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
    Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Guid?> RotateAsync(string oldHash, string newHash, DateTime expiresAt, CancellationToken cancellationToken = default);
}

/// <summary>Atomically records a payment without ever exceeding the order balance.</summary>
public interface IPaymentStore
{
    Task<bool> RecordAsync(Guid shopId, Guid orderId, decimal amount, PaymentMethod method,
        string? reference, string recordedBy, CancellationToken cancellationToken = default);
}

/// <summary>Atomically settles an invoice and advances the subscription once.</summary>
public record BillingDeadlineResult(int OverdueInvoices, int ShopsFlagged);

public interface IInvoiceStore
{
    Task<bool> SettleAsync(Guid shopId, Guid invoiceId, PaymentMethod method, string? reference,
        CancellationToken cancellationToken = default);

    /// <summary>Single authoritative transition for overdue invoices and expired terms.</summary>
    Task<BillingDeadlineResult> ApplyDeadlinesAsync(DateTime now, Guid? shopId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Gives an older shop with no invoices a payable current-period invoice.</summary>
    Task EnsureCurrentInvoiceAsync(Guid shopId, decimal monthlyPrice, CancellationToken cancellationToken = default);
}
