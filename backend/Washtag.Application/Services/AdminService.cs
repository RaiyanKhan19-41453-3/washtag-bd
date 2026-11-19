using Microsoft.EntityFrameworkCore;
using Washtag.Application.DTOs;
using Washtag.Application.Interfaces;
using Washtag.Domain.Enums;
using Washtag.Domain.Exceptions;

namespace Washtag.Application.Services;

public class AdminService : IAdminService
{
    private readonly IAppDbContext _db;

    public AdminService(IAppDbContext db) => _db = db;

    public async Task<AdminStatsDto> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var overdue = await _db.Invoices
            .Where(i => i.Status == InvoiceStatus.Pending && i.PeriodTo < now).ToListAsync(cancellationToken);
        if (overdue.Count > 0)
        {
            foreach (var invoice in overdue) invoice.Status = InvoiceStatus.Overdue;
            var affectedIds = overdue.Select(i => i.ShopId).ToHashSet();
            var affected = await _db.Shops.Where(s => affectedIds.Contains(s.Id)).ToListAsync(cancellationToken);
            foreach (var shop in affected)
                if (shop.SubscriptionStatus is SubscriptionStatus.Trial or SubscriptionStatus.Active)
                    shop.SubscriptionStatus = SubscriptionStatus.PastDue;
            await _db.SaveChangesAsync(cancellationToken);
        }

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
        shop.IsActive = active;
        if (active && shop.SubscriptionStatus == SubscriptionStatus.Cancelled)
            shop.SubscriptionStatus = SubscriptionStatus.Active;
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
}
