using Microsoft.EntityFrameworkCore;
using Washtag.Application.DTOs;
using Washtag.Domain;
using Washtag.Application.Interfaces;
using Washtag.Domain.Entities;
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
        var now = DateTime.UtcNow;
        shop.SubscriptionStatus = SubscriptionStatus.Active;
        shop.IsActive = true;
        if (shop.SubscriptionEndsAt <= now)
            shop.SubscriptionEndsAt = now.AddMonths(1);

        // A restored term needs something payable, otherwise the shop is active but
        // has no way to renew when that term ends.
        if (!await _db.Invoices.AnyAsync(
                i => i.ShopId == shopId && i.Status == InvoiceStatus.Pending && i.PeriodTo > now,
                cancellationToken))
        {
            var from = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            _db.Invoices.Add(new Invoice
            {
                ShopId = shopId,
                InvoiceNumber = $"INV-{from:yyyyMM}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
                PeriodFrom = from,
                PeriodTo = shop.SubscriptionEndsAt,
                Amount = Plans.MonthlyPrice(shop.Plan),
                Status = InvoiceStatus.Pending,
                CreatedAt = now
            });
        }
        await _db.SaveChangesAsync(cancellationToken);
        return AuthService.MapShop(shop);
    }

    public async Task<InvoiceDto> SettleInvoiceAsync(Guid shopId, Guid invoiceId, PaymentMethod method,
        string? reference, CancellationToken cancellationToken = default)
    {
        var settled = await _invoices.SettleAsync(shopId, invoiceId, method, reference, cancellationToken);
        if (!settled) throw new AppException("The invoice state changed. Reload and try again.");
        var invoice = await _db.Invoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.ShopId == shopId, cancellationToken)
            ?? throw new AppException("Invoice not found.");
        return new InvoiceDto
        {
            Id = invoice.Id, InvoiceNumber = invoice.InvoiceNumber,
            PeriodFrom = invoice.PeriodFrom, PeriodTo = invoice.PeriodTo,
            Amount = invoice.Amount, Status = invoice.Status, PaidAt = invoice.PaidAt,
            Method = invoice.Method, Reference = invoice.Reference, Notes = invoice.Notes
        };
    }

    public async Task<InvoiceDto?> SettleLatestInvoiceAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        var invoice = await _db.Invoices.AsNoTracking()
            .Where(i => i.ShopId == shopId && i.Status == InvoiceStatus.Pending)
            .OrderBy(i => i.PeriodTo).FirstOrDefaultAsync(cancellationToken);
        if (invoice == null) return null;
        return await SettleInvoiceAsync(shopId, invoice.Id, PaymentMethod.Cash, "Admin settlement", cancellationToken);
    }
}
