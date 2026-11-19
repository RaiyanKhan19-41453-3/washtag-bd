using Microsoft.EntityFrameworkCore;
using Washtag.Application.DTOs;
using Washtag.Application.Interfaces;
using Washtag.Domain.Enums;
using Washtag.Domain.Exceptions;

namespace Washtag.Application.Services;

public class AdminService : IAdminService
{
    private readonly IAppDbContext _db;
    private readonly IInvoiceStore _invoices;

    public AdminService(IAppDbContext db, IInvoiceStore invoices)
    {
        _db = db;
        _invoices = invoices;
    }

    public async Task<AdminStatsDto> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        await _invoices.ApplyDeadlinesAsync(DateTime.UtcNow, null, cancellationToken);

        var shops = await _db.Shops.AsNoTracking().ToListAsync(cancellationToken);
        var users = await _db.Users.AsNoTracking().ToListAsync(cancellationToken);
        var orders = await _db.Orders.AsNoTracking().ToListAsync(cancellationToken);

        var list = shops.Select(shop =>
        {
            var shopOrders = orders.Where(o => o.ShopId == shop.Id).ToList();
            return new AdminShopDto
            {
                ShopId = shop.Id,
                ShopName = shop.Name,
                OwnerName = shop.OwnerName,
                Email = shop.Email,
                City = shop.City,
                Plan = shop.Plan,
                SubscriptionStatus = shop.SubscriptionStatus,
                RenewalDate = shop.SubscriptionEndsAt,
                IsActive = shop.IsActive,
                UserCount = users.Count(u => u.ShopId == shop.Id),
                OrderCount = shopOrders.Count,
                Collected = shopOrders.Sum(o => o.PaidAmount)
            };
        }).OrderByDescending(s => s.Collected).ToList();

        return new AdminStatsDto
        {
            ShopCount = shops.Count,
            ActiveShopCount = shops.Count(s => s.IsActive),
            UserCount = users.Count,
            OrderCount = orders.Count,
            Collected = orders.Sum(o => o.PaidAmount),
            Shops = list
        };
    }

    public async Task<ShopDto> SetShopActiveAsync(Guid shopId, bool active, CancellationToken cancellationToken = default)
    {
        var shop = await _db.Shops.FirstOrDefaultAsync(s => s.Id == shopId, cancellationToken)
            ?? throw new AppException("Shop not found.");
        // Suspend/reactivate only toggles access. It must never change billing state:
        // otherwise a cancelled shop could be resurrected by a suspend/reactivate pair.
        shop.IsActive = active;
        await _db.SaveChangesAsync(cancellationToken);
        return AuthService.MapShop(shop);
    }

    public async Task<ShopDto> CancelShopSubscriptionAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        var shop = await _db.Shops.FirstOrDefaultAsync(s => s.Id == shopId, cancellationToken)
            ?? throw new AppException("Shop not found.");
        shop.SubscriptionStatus = SubscriptionStatus.Cancelled;
        await _db.SaveChangesAsync(cancellationToken);
        return AuthService.MapShop(shop);
    }

    public async Task<ShopDto> RestoreSubscriptionAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        var shop = await _db.Shops.FirstOrDefaultAsync(s => s.Id == shopId, cancellationToken)
            ?? throw new AppException("Shop not found.");
        shop.SubscriptionStatus = SubscriptionStatus.Active;
        if (shop.SubscriptionEndsAt <= DateTime.UtcNow)
            shop.SubscriptionEndsAt = DateTime.UtcNow.AddMonths(1);
        await _db.SaveChangesAsync(cancellationToken);
        return AuthService.MapShop(shop);
    }
}
