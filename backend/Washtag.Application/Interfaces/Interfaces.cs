using Microsoft.EntityFrameworkCore;
using Washtag.Application.DTOs;
using Washtag.Domain.Entities;

namespace Washtag.Application.Interfaces;

/// <summary>Abstraction over EF Core so the application layer never references infrastructure.</summary>
public interface IAppDbContext
{
    DbSet<Shop> Shops { get; }
    DbSet<AppUser> Users { get; }
    DbSet<Customer> Customers { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<StatusEvent> StatusEvents { get; }
    DbSet<Payment> Payments { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<AuthResponse> GetProfileAsync(Guid userId);
}

public interface IOrderService
{
    Task<PagedResult<OrderListDto>> GetOrdersAsync(Guid shopId, string? status, string? search, int page, int pageSize);
    Task<OrderDetailDto> GetOrderAsync(Guid shopId, Guid id);
    Task<OrderDetailDto> GetOrderByTokenAsync(Guid shopId, string token);
    Task<OrderDetailDto> CreateOrderAsync(Guid shopId, CreateOrderRequest request, string actor);
    Task<OrderDetailDto> UpdateStatusAsync(Guid shopId, Guid id, UpdateStatusRequest request, string actor);
    Task<OrderDetailDto> RecordPaymentAsync(Guid shopId, Guid id, RecordPaymentRequest request, string actor);
    Task CancelOrderAsync(Guid shopId, Guid id, string actor);
    Task<PublicTrackDto?> TrackAsync(string token);
    Task<byte[]> GetQrPngAsync(Guid shopId, Guid id);
    Task<DashboardDto> GetDashboardAsync(Guid shopId);
    Task<List<CustomerDto>> GetCustomersAsync(Guid shopId);
    Task<ShopDto> UpdateShopAsync(Guid shopId, UpdateShopRequest request);
}

public interface IRealtimeNotifier
{
    Task OrderUpdatedAsync(Order order, object ownerPayload, object customerPayload);
}

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value, TimeSpan expiration);
    Task RemoveAsync(string key);
}

public interface IQrCodeService
{
    string GenerateDataUrl(string payload);
    byte[] GeneratePng(string payload);
}

public interface IJwtTokenGenerator
{
    string Generate(AppUser user, Shop shop);
}

public interface ITrackingUrlProvider
{
    string Build(Guid token);
}
