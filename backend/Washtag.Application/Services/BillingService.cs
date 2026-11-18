using Microsoft.EntityFrameworkCore;
using Washtag.Application.DTOs;
using Washtag.Application.Interfaces;
using Washtag.Domain;
using Washtag.Domain.Entities;
using Washtag.Domain.Enums;
using Washtag.Domain.Exceptions;

namespace Washtag.Application.Services;

public class BillingService : IBillingService
{
    private readonly IAppDbContext _db;

    public BillingService(IAppDbContext db) => _db = db;

    public async Task<BillingOverviewDto> GetOverviewAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        var shop = await _db.Shops.FirstOrDefaultAsync(s => s.Id == shopId, cancellationToken)
            ?? throw new AppException("Shop not found.");
        var invoices = await _db.Invoices.AsNoTracking()
            .Where(i => i.ShopId == shopId)
            .OrderByDescending(i => i.PeriodTo)
            .ToListAsync(cancellationToken);

        return new BillingOverviewDto
        {
            Plan = shop.Plan,
            Status = shop.SubscriptionStatus,
            StartedAt = shop.SubscriptionStartedAt,
            RenewalDate = shop.SubscriptionEndsAt,
            MonthlyPrice = Plans.MonthlyPrice(shop.Plan),
            Outstanding = invoices.Where(i => i.Status is InvoiceStatus.Pending or InvoiceStatus.Overdue)
                .Sum(i => i.Amount),
            Invoices = invoices.Select(Map).ToList()
        };
    }

    public async Task<InvoiceDto> MarkPaidAsync(Guid shopId, Guid invoiceId, MarkInvoicePaidRequest request, CancellationToken cancellationToken = default)
    {
        var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId && i.ShopId == shopId, cancellationToken)
            ?? throw new AppException("Invoice not found.");
        if (invoice.Status == InvoiceStatus.Paid)
            throw new AppException("This invoice is already paid.");

        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidAt = DateTime.UtcNow;
        invoice.Method = request.Method;
        invoice.Reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim();

        var remaining = await _db.Invoices.CountAsync(
            i => i.ShopId == shopId && i.Id != invoiceId &&
                 i.Status != InvoiceStatus.Paid && i.Status != InvoiceStatus.Cancelled, cancellationToken);
        if (remaining == 0)
        {
            var shop = await _db.Shops.FirstOrDefaultAsync(s => s.Id == shopId, cancellationToken);
            if (shop != null) shop.SubscriptionStatus = SubscriptionStatus.Active;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Map(invoice);
    }

    public async Task<BillingOverviewDto> ChangePlanAsync(Guid shopId, ChangePlanRequest request, CancellationToken cancellationToken = default)
    {
        if (!Plans.IsValid(request.Plan))
            throw new AppException("Choose one of: Starter, Growth or Pro.");

        var shop = await _db.Shops.FirstOrDefaultAsync(s => s.Id == shopId, cancellationToken)
            ?? throw new AppException("Shop not found.");
        var plan = PlanName(request.Plan);
        var price = Plans.MonthlyPrice(plan);

        shop.Plan = plan;
        var pending = await _db.Invoices
            .Where(i => i.ShopId == shopId && i.Status == InvoiceStatus.Pending && i.PeriodTo > DateTime.UtcNow)
            .ToListAsync(cancellationToken);
        foreach (var invoice in pending) invoice.Amount = price;

        await _db.SaveChangesAsync(cancellationToken);
        return await GetOverviewAsync(shopId, cancellationToken);
    }

    private static string PlanName(string value) => Plans.All.First(p =>
        p.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));

    private static InvoiceDto Map(Invoice invoice) => new()
    {
        Id = invoice.Id,
        InvoiceNumber = invoice.InvoiceNumber,
        PeriodFrom = invoice.PeriodFrom,
        PeriodTo = invoice.PeriodTo,
        Amount = invoice.Amount,
        Status = invoice.Status,
        PaidAt = invoice.PaidAt,
        Method = invoice.Method,
        Reference = invoice.Reference,
        Notes = invoice.Notes
    };
}
