using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Washtag.Domain;
using Washtag.Domain.Entities;
using Washtag.Domain.Enums;

namespace Washtag.Infrastructure.Data;

/// <summary>Creates sample subscription invoices for shops that have none.</summary>
public static class BillingSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var shops = await db.Shops.ToListAsync();

        foreach (var shop in shops)
        {
            if (await db.Invoices.AnyAsync(i => i.ShopId == shop.Id)) continue;

            var price = Plans.MonthlyPrice(shop.Plan);
            var currentMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var suffix = shop.Id.ToString("N")[..6].ToUpperInvariant();

            for (var offset = -2; offset <= 0; offset++)
            {
                var from = currentMonth.AddMonths(offset);
                var paid = offset < 0;
                db.Invoices.Add(new Invoice
                {
                    ShopId = shop.Id,
                    InvoiceNumber = $"INV-{from:yyyyMM}-{suffix}",
                    PeriodFrom = from,
                    PeriodTo = from.AddMonths(1),
                    Amount = price,
                    Status = paid ? InvoiceStatus.Paid : InvoiceStatus.Pending,
                    PaidAt = paid ? from.AddDays(3) : null,
                    Method = paid ? PaymentMethod.Bkash : PaymentMethod.Cash,
                    Reference = paid ? $"BK-{suffix}-{Math.Abs(offset)}" : null,
                    Notes = paid ? null : "Current billing period"
                });
            }
            // Seeding invoices must never overwrite a real subscription state.
        }

        await db.SaveChangesAsync();
    }
}
