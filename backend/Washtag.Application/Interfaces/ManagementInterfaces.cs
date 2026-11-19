using Washtag.Application.DTOs;
using Washtag.Domain.Enums;

namespace Washtag.Application.Interfaces;

public interface IReportService
{
    Task<DailyReportDto> DailyAsync(Guid shopId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task<byte[]> OrdersCsvAsync(Guid shopId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
}

public interface IServiceCatalogService
{
    Task<List<ServiceDto>> ListAsync(Guid shopId, CancellationToken cancellationToken = default);
    Task<ServiceDto> CreateAsync(Guid shopId, CreateServiceRequest request, CancellationToken cancellationToken = default);
    Task<ServiceDto> UpdateAsync(Guid shopId, Guid id, UpdateServiceRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid shopId, Guid id, CancellationToken cancellationToken = default);
}

/// <summary>Configures which email addresses get platform-admin privileges.</summary>
public interface IAdminSettings
{
    bool IsAdmin(string email);
}

public interface IStaffService
{
    Task<List<StaffDto>> ListAsync(Guid shopId, CancellationToken cancellationToken = default);
    Task<StaffDto> CreateAsync(Guid shopId, CreateStaffRequest request, CancellationToken cancellationToken = default);
    Task<StaffDto> UpdateAsync(Guid shopId, Guid userId, UpdateStaffRequest request, CancellationToken cancellationToken = default);
    Task DeactivateAsync(Guid shopId, Guid userId, CancellationToken cancellationToken = default);
}

public interface IBillingService
{
    Task<BillingOverviewDto> GetOverviewAsync(Guid shopId, CancellationToken cancellationToken = default);
    Task<InvoiceDto> MarkPaidAsync(Guid shopId, Guid invoiceId, MarkInvoicePaidRequest request, CancellationToken cancellationToken = default);
    Task<BillingOverviewDto> ChangePlanAsync(Guid shopId, ChangePlanRequest request, CancellationToken cancellationToken = default);
}

public interface IAdminService
{
    Task<AdminStatsDto> GetStatsAsync(CancellationToken cancellationToken = default);
    Task<ShopDto> SetShopActiveAsync(Guid shopId, bool active, CancellationToken cancellationToken = default);
    Task<ShopDto> CancelShopSubscriptionAsync(Guid shopId, CancellationToken cancellationToken = default);
    Task<ShopDto> RestoreSubscriptionAsync(Guid shopId, CancellationToken cancellationToken = default);
    Task<InvoiceDto> SettleInvoiceAsync(Guid shopId, Guid invoiceId, PaymentMethod method, string? reference,
        CancellationToken cancellationToken = default);
    Task<InvoiceDto?> SettleLatestInvoiceAsync(Guid shopId, CancellationToken cancellationToken = default);
}

/// <summary>Idempotent housekeeping: billing deadlines, retention and vector freshness.</summary>
public interface IMaintenanceService
{
    Task<MaintenanceResultDto> RunAsync(CancellationToken cancellationToken = default);
}
