using Microsoft.EntityFrameworkCore;
using Washtag.Application.DTOs;
using Washtag.Application.Interfaces;

namespace Washtag.Application.Services;

public class AdminService : IAdminService
{
    private readonly IAppDbContext _db;

    public AdminService(IAppDbContext db) => _db = db;

    public async Task<AdminStatsDto> GetStatsAsync(CancellationToken cancellationToken = default)
    {
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
}
